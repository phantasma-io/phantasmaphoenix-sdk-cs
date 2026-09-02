using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.RPC.Models;

namespace PhantasmaPhoenix.RPC;

/// <summary>A transaction the chain would reject after charging for it. The pre-flight catches it
/// before signing.</summary>
public class TransactionPreflightException : Exception
{
	public TransactionPreflightException(string message) : base(message) { }
}

/// <summary>The lookups a pre-flight needs. A <see cref="PhantasmaAPI"/> provides them, and so does
/// anything with the same shape.</summary>
public interface IPreflightSource
{
	Task<TokenResult?> GetTokenAsync(string symbol, bool extended, ulong carbonTokenId);

	/// <summary>
	/// Id of a token that certainly exists on this chain, which is the gas token. The symbol lookup
	/// needs it as a CONTROL, see <see cref="TransactionPreflight.PreflightAsync"/>. A source that
	/// cannot supply one still works. A free symbol is then reported as
	/// <see cref="PreflightVerdict.Unknown"/>.
	/// </summary>
	Task<ulong?> ControlTokenIdAsync();
}

/// <summary>What a pre-flight established.</summary>
public enum PreflightVerdict
{
	/// <summary>The message is not a token creation, so there is nothing to check.</summary>
	NotApplicable,
	/// <summary>The chain answered that the symbol is in use.</summary>
	Taken,
	/// <summary>The chain answered through the control that the symbol is not in use.</summary>
	Free,
	/// <summary>The lookup did not answer. Nothing follows from it. It does not mean
	/// <see cref="Free"/>.</summary>
	Unknown
}

public sealed class PreflightResult
{
	public PreflightVerdict Verdict { get; }

	/// <summary>What was being checked, for example <c>token symbol GPX</c>. Null when nothing was
	/// checked.</summary>
	public string? Subject { get; }

	/// <summary>Why the lookup established nothing, in the node's own words. Present only on
	/// <see cref="PreflightVerdict.Unknown"/>.</summary>
	public string? Reason { get; }

	public PreflightResult(PreflightVerdict verdict, string? subject = null, string? reason = null)
	{
		Verdict = verdict;
		Subject = subject;
		Reason = reason;
	}
}

public static class TransactionPreflight
{
	/// <summary>
	/// Asks the chain whether the symbol a CreateToken claims is already in use. One lookup answers it.
	/// <para>
	/// The check is worth making because the call consumes its policy fee before the contract looks at
	/// the symbol. That fee is the largest single price in the protocol. Governance sets it and
	/// getGasConfig reports it. Sending a symbol that is taken pays it for nothing.
	/// </para>
	/// <para>
	/// A symbol that resolves to a token is <see cref="PreflightVerdict.Taken"/>. A symbol that does
	/// not resolve comes back as an ordinary RPC error. The node reports a missing method and a failed
	/// backend the same way. Every one of them arrives as the same internal error code with prose for a
	/// message, so an error alone never means absence.
	/// </para>
	/// <para>
	/// The check therefore asks a second question whose answer it already knows. It fetches the CONTROL
	/// token by its id, and the node resolves that without touching the symbol at all. A node that
	/// answers the control is answering questions, so its refusal about the caller's symbol is a real
	/// absence and the verdict is <see cref="PreflightVerdict.Free"/>. A node that does not answer the
	/// control has established nothing, and the verdict is <see cref="PreflightVerdict.Unknown"/>.
	/// Without a control every absent symbol is Unknown.
	/// </para>
	/// <para>
	/// The control proves the node is serving token lookups. It does not exercise symbol resolution
	/// itself, so a node whose token rows read while its symbol index does not would still be believed.
	/// That is the residual, and it is a far narrower one than trusting an error message.
	/// </para>
	/// <para>
	/// The verdict is reported here and acted on elsewhere.
	/// <see cref="PhantasmaAPI.SendTransactionAsync(TxMsg, IReadOnlyList{ITxSigner}, SendTransactionOptions?, CancellationToken)"/>
	/// refuses on Taken and on Unknown. The builders enforce the message's own validity: flags,
	/// metadata, schemas. This method covers the part only the chain can answer.
	/// </para>
	/// </summary>
	public static async Task<PreflightResult> PreflightAsync(IPreflightSource source, TxMsg msg)
	{
		if (source == null) throw new ArgumentNullException(nameof(source));
		if (msg.type != TxTypes.Call) return new PreflightResult(PreflightVerdict.NotApplicable);
		var call = (TxMsgCall)msg.msg;
		if (call.moduleId != (uint)ModuleId.Token || call.methodId != (uint)TokenContract_Methods.CreateToken)
			return new PreflightResult(PreflightVerdict.NotApplicable);

		var symbol = CarbonBlob.New<TokenInfo>(call.args).symbol.data;
		if (string.IsNullOrEmpty(symbol)) return new PreflightResult(PreflightVerdict.NotApplicable);
		var subject = $"token symbol {symbol}";

		var token = await LookUpAsync(() => source.GetTokenAsync(symbol, false, 0)).ConfigureAwait(false);
		// A token came back, so the symbol resolves to one. Nothing else is read from it: the question
		// was only whether it exists.
		if (token.Ok) return new PreflightResult(PreflightVerdict.Taken, subject);

		var control = await source.ControlTokenIdAsync().ConfigureAwait(false);
		if (control == null || control.Value == 0)
			return new PreflightResult(PreflightVerdict.Unknown, subject, token.Reason);
		var probe = await LookUpAsync(() => source.GetTokenAsync(string.Empty, false, control.Value)).ConfigureAwait(false);
		return probe.Ok
			? new PreflightResult(PreflightVerdict.Free, subject)
			: new PreflightResult(PreflightVerdict.Unknown, subject, probe.Reason);
	}

	private readonly struct LookUp
	{
		public readonly bool Ok;
		public readonly string? Reason;
		public LookUp(bool ok, string? reason) { Ok = ok; Reason = reason; }
	}

	// A lookup counts as answered only when it returns a token. A thrown RPC error and a transport
	// failure mean the same thing here: the question was not answered. Neither is inspected further.
	// Nothing in either of them separates "there is no such symbol" from "this node could not tell
	// you".
	private static async Task<LookUp> LookUpAsync(Func<Task<TokenResult?>> call)
	{
		try
		{
			var value = await call().ConfigureAwait(false);
			return value != null ? new LookUp(true, null) : new LookUp(false, "the lookup returned nothing");
		}
		catch (Exception error)
		{
			return new LookUp(false, string.IsNullOrEmpty(error.Message) ? "the lookup failed" : error.Message);
		}
	}
}
