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
using SafeExamBrowser.Linux.Browser.Filtering;

namespace SafeExamBrowser.Linux.Tests.Browser
{
	public class RequestFilterTests
	{
		private readonly RequestFilter sut = new RequestFilter();

		[Fact]
		public void MustProcessBlockRulesFirst()
		{
			var allow = new FakeRule(FilterResult.Allow, isMatch: true);
			var block = new FakeRule(FilterResult.Block, isMatch: true);

			sut.Load(allow);
			sut.Load(block);

			var result = sut.Process(new Request());

			Assert.Equal(0, allow.MatchCount);
			Assert.Equal(1, block.MatchCount);
			Assert.Equal(FilterResult.Block, result);
		}

		[Fact]
		public void MustProcessAllowRulesSecond()
		{
			var allow = new FakeRule(FilterResult.Allow, isMatch: true);
			var block = new FakeRule(FilterResult.Block, isMatch: false);

			sut.Load(allow);
			sut.Load(block);

			var result = sut.Process(new Request());

			Assert.Equal(1, allow.MatchCount);
			Assert.Equal(1, block.MatchCount);
			Assert.Equal(FilterResult.Allow, result);
		}

		[Fact]
		public void MustReturnDefaultWithoutMatch()
		{
			var allow = new FakeRule(FilterResult.Allow, isMatch: false);
			var block = new FakeRule(FilterResult.Block, isMatch: false);

			sut.Default = (FilterResult) (-1);
			sut.Load(allow);
			sut.Load(block);

			var result = sut.Process(new Request());

			Assert.Equal(1, allow.MatchCount);
			Assert.Equal(1, block.MatchCount);
			Assert.Equal((FilterResult) (-1), result);
		}

		[Fact]
		public void MustReturnDefaultWithoutRules()
		{
			sut.Default = FilterResult.Allow;
			Assert.Equal(FilterResult.Allow, sut.Process(new Request()));

			sut.Default = FilterResult.Block;
			Assert.Equal(FilterResult.Block, sut.Process(new Request()));
		}

		[Fact]
		public void MustNotAllowUnsupportedResult()
		{
			var rule = new FakeRule((FilterResult) (-1), isMatch: false);

			Assert.Throws<NotImplementedException>(() => sut.Load(rule));
		}

		private sealed class FakeRule : IRule
		{
			private readonly bool isMatch;

			public int MatchCount { get; private set; }

			public FilterResult Result { get; }

			public FakeRule(FilterResult result, bool isMatch)
			{
				Result = result;
				this.isMatch = isMatch;
			}

			public void Initialize(string expression, FilterResult result)
			{
			}

			public bool IsMatch(Request request)
			{
				MatchCount++;

				return isMatch;
			}
		}
	}
}
