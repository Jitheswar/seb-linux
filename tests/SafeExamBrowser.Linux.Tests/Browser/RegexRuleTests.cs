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
using Xunit;
using SafeExamBrowser.Linux.Browser.Filtering;
using SafeExamBrowser.Linux.Browser.Filtering.Rules;

namespace SafeExamBrowser.Linux.Tests.Browser
{
	public class RegexRuleTests
	{
		private RegexRule sut;

		public RegexRuleTests() { sut = new RegexRule(); }

		[Fact]
		public void MustIgnoreCase()
		{
			sut.Initialize(Regex.Escape("http://www.test.org/path/file.txt?param=123"), FilterResult.Allow);

			Assert.True(sut.IsMatch(new Request { Url = "hTtP://wWw.TeSt.OrG/pAtH/fIlE.tXt?PaRaM=123" }));
			Assert.True(sut.IsMatch(new Request { Url = "HtTp://WwW.tEst.oRg/PaTh/FiLe.TxT?pArAm=123" }));

			sut.Initialize(Regex.Escape("HTTP://WWW.TEST.ORG/PATH/FILE.TXT?PARAM=123"), FilterResult.Allow);

			Assert.True(sut.IsMatch(new Request { Url = "hTtP://wWw.TeSt.OrG/pAtH/fIlE.tXt?PaRaM=123" }));
			Assert.True(sut.IsMatch(new Request { Url = "HtTp://WwW.tEst.oRg/PaTh/FiLe.TxT?pArAm=123" }));
		}

		[Fact]
		public void MustInitializeResult()
		{
			foreach (var result in Enum.GetValues(typeof(FilterResult)))
			{
				sut.Initialize("", (FilterResult) result);
				Assert.Equal(result, sut.Result);
			}
		}

		[Fact]
		public void MustNotAllowUndefinedExpression()
		{
			Assert.Throws<ArgumentNullException>(() => sut.Initialize(null, FilterResult.Allow));
		}

		[Fact]
		public void MustValidateExpression()
		{
			Assert.Throws<ArgumentException>(() => sut.Initialize("ç+\"}%&*/(+)=?{=*+¦]@#°§]`?´^¨'°[¬|¢", FilterResult.Allow));
		}
	}
}
