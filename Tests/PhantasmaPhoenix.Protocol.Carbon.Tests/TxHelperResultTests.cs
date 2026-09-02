using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// The creation calls answer with a bare integer: CreateToken with the u64 token id, CreateTokenSeries
// with the u32 series id. Both are little-endian on the wire, so a reader that took the wrong width
// would return the low half of the id and would not fail.
public class TxHelperResultTests
{
	[Fact]
	public void CreateToken_result_is_read_across_the_whole_64_bit_range()
	{
		// 0x0807060504030201 little-endian: the high half is nonzero, so a u32 reader could not agree.
		CreateTokenTxHelper.ParseResult("0102030405060708").ShouldBe(0x0807060504030201UL);
		CreateTokenTxHelper.ParseResult("2a00000000000000").ShouldBe(42UL);
		CreateTokenTxHelper.ParseResult("ffffffffffffffff").ShouldBe(ulong.MaxValue);
	}

	[Fact]
	public void CreateTokenSeries_result_is_read_as_the_32_bit_value_it_is()
	{
		CreateTokenSeriesTxHelper.ParseResult("01020304").ShouldBe(0x04030201U);
		CreateTokenSeriesTxHelper.ParseResult("ffffffff").ShouldBe(uint.MaxValue);
	}
}
