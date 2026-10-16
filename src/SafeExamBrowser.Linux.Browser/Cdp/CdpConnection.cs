/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port: communicates with the system browser via the
 * Chrome DevTools Protocol (https://chromedevtools.github.io/devtools-protocol/).
 */

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SafeExamBrowser.Linux.Browser.Cdp
{
	/// <summary>
	/// An event received via the DevTools protocol.
	/// </summary>
	internal sealed class CdpEvent
	{
		/// <summary>
		/// The session which emitted the event, or null for browser-level events.
		/// </summary>
		public string SessionId { get; set; }

		/// <summary>
		/// The event method, e.g. "Fetch.requestPaused".
		/// </summary>
		public string Method { get; set; }

		/// <summary>
		/// The event parameters.
		/// </summary>
		public JsonElement Parameters { get; set; }
	}

	internal sealed class CdpException : Exception
	{
		internal CdpException(string method, string message) : base($"DevTools command '{method}' failed: {message}")
		{
		}
	}

	/// <summary>
	/// A minimal Chrome DevTools Protocol client for a single WebSocket endpoint (the browser
	/// endpoint; sessions for individual targets are multiplexed via the 'sessionId' field).
	/// </summary>
	internal sealed class CdpConnection : IDisposable
	{
		private static readonly JsonSerializerOptions SERIALIZER_OPTIONS = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		private static readonly bool TRACE = Environment.GetEnvironmentVariable("SEB_CDP_TRACE") == "1";

		private readonly ClientWebSocket socket = new ClientWebSocket();
		private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
		private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new ConcurrentDictionary<int, TaskCompletionSource<JsonElement>>();
		private Task receiveLoop;
		private int lastId;

		/// <summary>
		/// Invoked sequentially (in delivery order) for every event received from the browser.
		/// </summary>
		internal event Action<CdpEvent> EventReceived;

		internal bool IsConnected => socket.State == WebSocketState.Open;

		internal async Task ConnectAsync(string webSocketUrl, CancellationToken cancellationToken = default)
		{
			await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);

			receiveLoop = Task.Run(ReceiveLoopAsync);
		}

		internal async Task<JsonElement> SendAsync(string method, object parameters = null, string sessionId = null, CancellationToken cancellationToken = default)
		{
			var id = Interlocked.Increment(ref lastId);
			var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

			if (!pending.TryAdd(id, completion))
			{
				throw new InvalidOperationException($"DevTools command ID {id} is already in use!");
			}

			try
			{
				var command = new Command
				{
					Id = id,
					Method = method,
					Parameters = parameters,
					SessionId = sessionId
				};
				var payload = JsonSerializer.SerializeToUtf8Bytes(command, SERIALIZER_OPTIONS);

				if (TRACE)
				{
					Console.Error.WriteLine($"CDP >> {Encoding.UTF8.GetString(payload)}");
				}

				await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

				return await completion.Task.WaitAsync(cancellationToken);
			}
			catch
			{
				pending.TryRemove(id, out _);

				throw;
			}
		}

		/// <summary>
		/// Sends a command and returns a task for the response. Unlike <see cref="SendAsync"/>, the
		/// returned task resolves as soon as the command has been written to the socket, so the
		/// response task can be collected independently. This is required for commands which are
		/// handled by the renderer, because the renderer may be blocked while a navigation request
		/// is paused by request interception.
		/// </summary>
		internal async Task<Task<JsonElement>> StartCommandAsync(string method, object parameters = null, string sessionId = null, CancellationToken cancellationToken = default)
		{
			var id = Interlocked.Increment(ref lastId);
			var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

			if (!pending.TryAdd(id, completion))
			{
				throw new InvalidOperationException($"DevTools command ID {id} is already in use!");
			}

			try
			{
				var command = new Command
				{
					Id = id,
					Method = method,
					Parameters = parameters,
					SessionId = sessionId
				};
				var payload = JsonSerializer.SerializeToUtf8Bytes(command, SERIALIZER_OPTIONS);

				if (TRACE)
				{
					Console.Error.WriteLine($"CDP >> {Encoding.UTF8.GetString(payload)}");
				}

				await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

				return completion.Task;
			}
			catch
			{
				pending.TryRemove(id, out _);

				throw;
			}
		}

		/// <summary>
		/// Sends a command without waiting for the response.
		/// </summary>
		internal void Post(string method, object parameters = null, string sessionId = null)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await SendAsync(method, parameters, sessionId);
				}
				catch (Exception)
				{
				}
			});
		}

		public void Dispose()
		{
			cancellation.Cancel();

			try
			{
				if (socket.State == WebSocketState.Open)
				{
					socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).Wait(2000);
				}
			}
			catch (Exception)
			{
			}

			socket.Dispose();
			cancellation.Dispose();
		}

		private async Task ReceiveLoopAsync()
		{
			var buffer = new byte[65536];
			var message = new MemoryStream();

			try
			{
				while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
				{
					var result = await socket.ReceiveAsync(buffer, cancellation.Token);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						break;
					}

					message.Write(buffer, 0, result.Count);

					while (!result.EndOfMessage)
					{
						result = await socket.ReceiveAsync(buffer, cancellation.Token);
						message.Write(buffer, 0, result.Count);
					}

					HandleMessage(message.ToArray());
					message.SetLength(0);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e) when (e is WebSocketException || e is IOException)
			{
				// The browser has been closed or the connection dropped.
			}
			finally
			{
				foreach (var completion in pending.Values)
				{
					completion.TrySetException(new InvalidOperationException("The connection to the browser has been lost!"));
				}

				pending.Clear();
			}
		}

		private void HandleMessage(byte[] json)
		{
			if (TRACE)
			{
				var text = Encoding.UTF8.GetString(json);

				Console.Error.WriteLine($"CDP << {(text.Length > 1000 ? text.Substring(0, 1000) + "..." : text)}");
			}

			JsonDocument document;

			try
			{
				document = JsonDocument.Parse(json);
			}
			catch (JsonException)
			{
				return;
			}

			using (document)
			{
				var root = document.RootElement;

				if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
				{
					HandleResponse(id, root);
				}
				else if (root.TryGetProperty("method", out var methodElement))
				{
					var @event = new CdpEvent
					{
						SessionId = root.TryGetProperty("sessionId", out var sessionElement) ? sessionElement.GetString() : default,
						Method = methodElement.GetString(),
						Parameters = root.TryGetProperty("params", out var parameters) ? parameters.Clone() : default
					};

					try
					{
						EventReceived?.Invoke(@event);
					}
					catch (Exception e)
					{
						Console.Error.WriteLine($"ERROR | Cdp    | Error while processing event '{@event.Method}': {e}");
					}
				}
			}
		}

		private void HandleResponse(int id, JsonElement root)
		{
			if (!pending.TryRemove(id, out var completion))
			{
				return;
			}

			if (root.TryGetProperty("error", out var error))
			{
				var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetInt32() : 0;
				var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "unknown error";

				completion.TrySetException(new CdpException($"#{id}", $"{message} (code {code})"));
			}
			else if (root.TryGetProperty("result", out var result))
			{
				completion.TrySetResult(result.Clone());
			}
			else
			{
				completion.TrySetResult(default);
			}
		}

		private sealed class Command
		{
			[JsonPropertyName("id")]
			public int Id { get; set; }

			[JsonPropertyName("method")]
			public string Method { get; set; }

			[JsonPropertyName("params")]
			public object Parameters { get; set; }

			[JsonPropertyName("sessionId")]
			public string SessionId { get; set; }
		}
	}
}
