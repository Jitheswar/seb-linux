/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System;
using System.Text.RegularExpressions;

namespace SafeExamBrowser.Linux.Browser.Filtering.Rules
{
	/// <summary>
	/// A filter rule based on a regular expression.
	/// </summary>
	internal class RegexRule : IRule
	{
		private string expression;

		public FilterResult Result { get; private set; }

		public void Initialize(string expression, FilterResult result)
		{
			ValidateExpression(expression);

			this.expression = expression;
			Result = result;
		}

		public bool IsMatch(Request request)
		{
			return Regex.IsMatch(request.Url, expression, RegexOptions.IgnoreCase);
		}

		private void ValidateExpression(string expression)
		{
			if (expression == default(string))
			{
				throw new ArgumentNullException(nameof(expression));
			}

			try
			{
				Regex.Match("", expression);
			}
			catch (Exception e)
			{
				throw new ArgumentException($"Invalid regular expression!", nameof(expression), e);
			}
		}
	}
}
