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
using System.Collections.Generic;

namespace SafeExamBrowser.Linux.Browser.Filtering
{
	/// <summary>
	/// The request filter as defined by the configuration, based on a set of block and allow rules.
	/// Block rules are always evaluated first, the default result is <see cref="FilterResult.Block"/>.
	/// </summary>
	internal class RequestFilter
	{
		private readonly IList<IRule> allowRules;
		private readonly IList<IRule> blockRules;

		public FilterResult Default { get; set; }

		public RequestFilter()
		{
			allowRules = new List<IRule>();
			blockRules = new List<IRule>();
			Default = FilterResult.Block;
		}

		public void Load(IRule rule)
		{
			switch (rule.Result)
			{
				case FilterResult.Allow:
					allowRules.Add(rule);
					break;
				case FilterResult.Block:
					blockRules.Add(rule);
					break;
				default:
					throw new NotImplementedException($"Filter processing for result '{rule.Result}' is not yet implemented!");
			}
		}

		public FilterResult Process(Request request)
		{
			foreach (var rule in blockRules)
			{
				if (rule.IsMatch(request))
				{
					return FilterResult.Block;
				}
			}

			foreach (var rule in allowRules)
			{
				if (rule.IsMatch(request))
				{
					return FilterResult.Allow;
				}
			}

			return Default;
		}
	}
}
