/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

namespace SafeExamBrowser.Linux.Browser.Filtering
{
	/// <summary>
	/// Defines all possible results of a request filter operation.
	/// </summary>
	public enum FilterResult
	{
		/// <summary>
		/// Indicates that a request should be allowed if a filter matches.
		/// </summary>
		Allow,

		/// <summary>
		/// Indicates that a request should be blocked if a filter matches.
		/// </summary>
		Block
	}

	/// <summary>
	/// Holds data relevant for filtering requests.
	/// </summary>
	public class Request
	{
		/// <summary>
		/// The full URL of the request.
		/// </summary>
		public string Url { get; set; }
	}

	/// <summary>
	/// Defines a filter rule which can be tested against a request.
	/// </summary>
	public interface IRule
	{
		/// <summary>
		/// The filter result to be used when the rule matches.
		/// </summary>
		FilterResult Result { get; }

		/// <summary>
		/// Initializes the rule with the given expression and filter result.
		/// </summary>
		void Initialize(string expression, FilterResult result);

		/// <summary>
		/// Determines whether the rule matches the given request.
		/// </summary>
		bool IsMatch(Request request);
	}
}
