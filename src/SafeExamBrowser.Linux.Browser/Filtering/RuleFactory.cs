/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using SafeExamBrowser.Linux.Browser.Filtering.Rules;

namespace SafeExamBrowser.Linux.Browser.Filtering
{
	/// <summary>
	/// Creates filter rules for the configured rule types.
	/// </summary>
	internal static class RuleFactory
	{
		public static IRule CreateRule(bool isRegex)
		{
			return isRegex ? new RegexRule() : (IRule) new SimplifiedRule();
		}
	}
}
