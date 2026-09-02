namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Who pays the gas. The native transaction types come in pairs. In the plain form the account moving
/// the tokens also pays the gas and signs alone. In the <c>_GasPayer</c> form a second account pays
/// the gas and both accounts sign. Setting <see cref="GasPayer"/> selects the second form.
/// </summary>
public class NativeTxParties : TxLimits
{
	/// <summary>The account whose tokens move. It is always a witness.</summary>
	public Bytes32 From { get; set; }

	/// <summary>A different account that pays the gas and becomes the first witness.</summary>
	public Bytes32? GasPayer { get; set; }
}

public sealed class TransferFungibleParams : NativeTxParties
{
	public Bytes32 To { get; set; }
	public ulong TokenId { get; set; }

	/// <summary>Amount in the token's atoms, as a u64. A big-fungible token needs a script transfer
	/// instead.</summary>
	public ulong Amount { get; set; }
}

public sealed class TransferNonFungibleParams : NativeTxParties
{
	public Bytes32 To { get; set; }
	public ulong TokenId { get; set; }

	/// <summary>The instances to move. One instance uses the single-instance type. Several use the
	/// multi-instance type.</summary>
	public ulong[] InstanceIds { get; set; } = Array.Empty<ulong>();
}

public sealed class MintFungibleParams : TxLimits
{
	/// <summary>The token owner. This account pays the gas and signs.</summary>
	public Bytes32 Owner { get; set; }
	public Bytes32 To { get; set; }
	public ulong TokenId { get; set; }

	/// <summary>
	/// Amount in the token's atoms. Mint and burn carry an <see cref="IntX"/>, because they also serve
	/// big-fungible tokens, whose balances do not fit a u64. <c>new IntX(long)</c> wraps an ordinary
	/// amount.
	/// </summary>
	public IntX Amount { get; set; }
}

public sealed class BurnFungibleParams : NativeTxParties
{
	public ulong TokenId { get; set; }

	/// <summary>Amount in the token's atoms, as an <see cref="IntX"/>. The reason is the same as for a
	/// mint.</summary>
	public IntX Amount { get; set; }
}

public sealed class BurnNonFungibleParams : NativeTxParties
{
	public ulong TokenId { get; set; }
	public ulong InstanceId { get; set; }
}

/// <summary>
/// Builders for the native transaction types. Those are the transfers, mints and burns that need no
/// VM script.
/// <para>
/// A builder assembles the message and nothing else. Fees are planned afterwards from the message
/// itself, with <c>PhantasmaAPI.Fees.PlanAsync</c> or <see cref="FeePlanner.Plan"/>. The witnesses
/// then sign with <see cref="TxMsgSigner"/>.
/// </para>
/// <para>
/// Without a <c>MaxGas</c> the message carries a zero offer, and it cannot be signed until it is
/// planned.
/// </para>
/// </summary>
public static class NativeTxHelper
{
	public static TxMsg TransferFungible(TransferFungibleParams p)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		if (p.GasPayer.HasValue)
		{
			return Base(TxTypes.TransferFungible_GasPayer, p.GasPayer.Value, p,
				new TxMsgTransferFungible_GasPayer { to = p.To, from = p.From, tokenId = p.TokenId, amount = p.Amount });
		}
		return Base(TxTypes.TransferFungible, p.From, p,
			new TxMsgTransferFungible { to = p.To, tokenId = p.TokenId, amount = p.Amount });
	}

	public static TxMsg TransferNonFungible(TransferNonFungibleParams p)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		if (p.InstanceIds == null || p.InstanceIds.Length == 0)
			throw new ArgumentException("InstanceIds must not be empty", nameof(p));
		var single = p.InstanceIds.Length == 1;
		if (p.GasPayer.HasValue)
		{
			return single
				? Base(TxTypes.TransferNonFungible_Single_GasPayer, p.GasPayer.Value, p,
					new TxMsgTransferNonFungible_Single_GasPayer { to = p.To, from = p.From, tokenId = p.TokenId, instanceId = p.InstanceIds[0] })
				: Base(TxTypes.TransferNonFungible_Multi_GasPayer, p.GasPayer.Value, p,
					new TxMsgTransferNonFungible_Multi_GasPayer { to = p.To, from = p.From, tokenId = p.TokenId, instanceIds = (ulong[])p.InstanceIds.Clone() });
		}
		return single
			? Base(TxTypes.TransferNonFungible_Single, p.From, p,
				new TxMsgTransferNonFungible_Single { to = p.To, tokenId = p.TokenId, instanceId = p.InstanceIds[0] })
			: Base(TxTypes.TransferNonFungible_Multi, p.From, p,
				new TxMsgTransferNonFungible_Multi { to = p.To, tokenId = p.TokenId, instanceIds = (ulong[])p.InstanceIds.Clone() });
	}

	public static TxMsg MintFungible(MintFungibleParams p)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		return Base(TxTypes.MintFungible, p.Owner, p,
			new TxMsgMintFungible { tokenId = p.TokenId, to = p.To, amount = p.Amount });
	}

	public static TxMsg BurnFungible(BurnFungibleParams p)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		if (p.GasPayer.HasValue)
		{
			return Base(TxTypes.BurnFungible_GasPayer, p.GasPayer.Value, p,
				new TxMsgBurnFungible_GasPayer { tokenId = p.TokenId, from = p.From, amount = p.Amount });
		}
		return Base(TxTypes.BurnFungible, p.From, p,
			new TxMsgBurnFungible { tokenId = p.TokenId, amount = p.Amount });
	}

	public static TxMsg BurnNonFungible(BurnNonFungibleParams p)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		if (p.GasPayer.HasValue)
		{
			return Base(TxTypes.BurnNonFungible_GasPayer, p.GasPayer.Value, p,
				new TxMsgBurnNonFungible_GasPayer { tokenId = p.TokenId, from = p.From, instanceId = p.InstanceId });
		}
		return Base(TxTypes.BurnNonFungible, p.From, p,
			new TxMsgBurnNonFungible { tokenId = p.TokenId, instanceId = p.InstanceId });
	}

	private static TxMsg Base(TxTypes type, Bytes32 gasFrom, TxLimits limits, object inner)
	{
		return TxLimits.Apply(new TxMsg
		{
			type = type,
			gasFrom = gasFrom,
			payload = SmallString.Empty,
			msg = inner
		}, limits);
	}
}
