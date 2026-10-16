/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SafeExamBrowser.Linux.Tests
{
	/// <summary>
	/// A request recorded by <see cref="StubHttpMessageHandler"/>.
	/// </summary>
	internal sealed class RecordedRequest
	{
		public string Body { get; set; }
		public IEnumerable<string> GetHeaders(string name) => Headers.TryGetValue(name, out var values) ? values : Enumerable.Empty<string>();
		public string Header(string name) => GetHeaders(name).FirstOrDefault();
		public Dictionary<string, List<string>> Headers { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		public string Method { get; set; }
		public string ContentType { get; set; }
		public string Uri { get; set; }

		public override string ToString() => $"{Method} {Uri}";
	}

	/// <summary>
	/// An HTTP message handler for tests which returns pre-configured responses and records
	/// all requests.
	/// </summary>
	internal sealed class StubHttpMessageHandler : HttpMessageHandler
	{
		private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();

		public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

		public StubHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder)
		{
			responders.Enqueue(responder);

			return this;
		}

		public StubHttpMessageHandler EnqueueJson(string content, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK, Action<HttpResponseMessage> configure = null)
		{
			return Enqueue(_ =>
			{
				var response = new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

				configure?.Invoke(response);

				return response;
			});
		}

		public RecordedRequest Last() => Requests.Last();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var recorded = new RecordedRequest
			{
				Method = request.Method.Method,
				Uri = request.RequestUri.ToString()
			};

			foreach (var header in request.Headers)
			{
				recorded.Headers[header.Key] = header.Value.ToList();
			}

			if (request.Content != default)
			{
				recorded.Body = await request.Content.ReadAsStringAsync(cancellationToken);
				recorded.ContentType = request.Content.Headers.ContentType?.MediaType;

				foreach (var header in request.Content.Headers)
				{
					recorded.Headers[header.Key] = header.Value.ToList();
				}
			}

			Requests.Add(recorded);

			if (responders.Count == 0)
			{
				throw new InvalidOperationException($"No responder configured for {recorded}!");
			}

			return responders.Dequeue()(request);
		}
	}
}
