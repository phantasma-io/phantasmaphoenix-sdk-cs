# phantasmaphoenix-sdk-cs
Phantasma Phoenix SDK for C#

## Carbon Transactions and Fees

Carbon transactions are built, planned, signed and sent as four separate steps, so that the same
message works whether the key is in memory, in a hardware wallet, or in a wallet reached over
Phantasma Link.

```csharp
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC;

using var api = new PhantasmaAPI("http://localhost:5172/rpc");
var keys = PhantasmaKeys.FromWIF(Environment.GetEnvironmentVariable("WIF")!);
var receiverPublicKey = new byte[32]; // the recipient's 32-byte public key

// 1. Build: the message only. Builders carry no prices; maxGas stays 0 until the fee is planned.
var transfer = NativeTxHelper.TransferFungible(new TransferFungibleParams
{
    From = new Bytes32(keys.PublicKey),
    To = new Bytes32(receiverPublicKey),
    TokenId = 1, // KCAL
    Amount = 100_000_000_000, // 10 KCAL in atoms (KCAL has ten decimals)
});

// 2. Plan: the gas bill and storage deposit for this message, from the chain's own prices.
var plan = await api.Fees.PlanAsync(transfer);
var summary = FeePlanSummary.Summarize(plan); // GasBill "0.00426", GasOffer "0.00426", StorageCeiling "0"

// 3-4. Sign and send - or let SendTransactionAsync do 2-4 in one call.
var hash = await api.SendTransactionAsync(transfer, keys);
```

Under gas model v2 there are two things to know, and `SendTransactionAsync` handles both for you:

1. **Plan before you sign.** The chain bills every byte the transaction puts in the block and
   escrows every storage row it creates, so a message carries a gas offer and a storage ceiling that
   have to be set before signing - a message signed with a zero offer is never admitted.
2. **What the plan cannot know, it assumes expensive.** Some of the price depends on chain state the
   message does not carry: whether the recipient already holds the token, which mode a series mints
   in. Each is taken at the value that costs MORE, because unused gas is refunded while a short
   offer is rejected. The plan is therefore a ceiling, not a prediction.

- `api.Fees` is the fee planner of the chain the client talks to. It reads `getGasConfig` once,
  keeps it for a minute, and prices every message from the message itself: the signed size is
  computed without a key, and the storage rows, call result bytes and gas sites of each native
  operation are priced with the chain's own formula (`FeePlanner.Plan`). `plan.Kind` says which
  operation was priced; the one kind that is a budget rather than a formula is
  `NativeFeeKind.Script` - VM scripts and unmodelled calls, whose work depends on execution.
- `FeePlanOptions` is where you tighten point 2 by telling the planner a fact it would otherwise
  assume. Every property is optional and most callers set none.
- A message the caller has already planned (`maxGas > 0`) is sent as it is. Signing an unplanned
  message is refused, because a zero offer is never admitted.
- `SendTransactionAsync` runs a pre-flight before signing a **token creation**. `Token.CreateToken`
  is charged its policy fee - the largest single price in the protocol, set by governance and
  readable from `getGasConfig` - **before** the contract checks whether the symbol is free, so
  sending one that is already taken costs that fee and gets nothing back. One lookup avoids it
  (`TransactionPreflight.PreflightAsync`). Every other message goes straight through.
- The pre-flight never reads a verdict out of an error message. A symbol that resolves is `Taken`;
  one that does not comes back as an ordinary RPC error, which a broken or proxied node produces
  just as readily, so the check asks a second question it knows the answer to - the gas token, by
  id, a lookup that does not touch symbols. If that answers, the node is answering and the symbol is
  `Free`; if it does not, the verdict is `Unknown` and nothing was established. `SendTransactionAsync`
  refuses on `Taken` and on `Unknown`. A caller who would rather decide for itself - warn before
  spending the fee, retry against another node - calls `TransactionPreflight.PreflightAsync`, reads
  the verdict and sends with `new SendTransactionOptions { Preflight = false }`.
- Any witness that implements `ITxSigner` (`PublicKey` + `SignAsync(bytes)`) can sign, and
  `TxMsgSigner.SignWithAsync` accepts several - the gas-payer transaction types take two. A wallet
  that signs elsewhere plans with `api.Fees.PlanAsync` and hands `plan.Apply(msg)` over.
- A contract call (`Call`, `Call_Multi`, `Trade`, `Phantasma`) chooses its own witnesses, and each
  one is 96 bytes the chain bills, so `FeePlanner.Plan` asks for `WitnessCount` instead of assuming
  one and under-offering every multi-party transaction. `SendTransactionAsync` fills it in from the
  signers it was given. The gas payer must be one of them - the chain rejects a transaction its
  payer did not sign.
- A message expires. Builders stamp `TxLimits.DefaultExpiryMs` from now, which is sized for a
  transaction signed on the spot; when a person has to approve it first, take the chain's whole
  window instead: `TxLimits.ExpiryWithin((await api.Fees.ChainParamsAsync()).ExpiryWindow)`.
- `UnitConversion.ToDecimalString` / `ToBigInteger` convert between atoms and decimal amounts (KCAL
  has 10 decimals, SOUL 8); `FeePlanSummary.Summarize` renders a plan in KCAL and SOUL for display.

## Release workflow (manifest-based)

This repository now supports a manifest-driven release flow for internal package dependencies.

### Files

- `release/release-manifest.json`
  - source of truth for:
    - package versions,
    - internal dependency floors (what each package must depend on),
    - publish order.
- `scripts/release-validate.sh`
  - validates manifest structure and checks:
    - csproj versions match manifest versions,
    - `publishOrder` is complete/stable (all package ids exactly once),
    - release intent against NuGet (`publish` flag and versions),
    - produced nuspec internal dependencies for `publish=true` packages match manifest.
- `scripts/release-smoke.sh`
  - packs packages locally and runs clean restore smoke tests for each `publish=true` package.
  - verifies that resolved internal transitive versions match manifest targets.
- `scripts/release-publish.sh`
  - publishes `.nupkg` files from `output/nupkgs` in manifest order.

### Commands

- Validate release metadata and nuspec floors:
  - `just release-validate`
- Run full release checks (validate + transitive smoke):
  - `just release-check`
- Publish in manifest order:
  - `just release-publish`

### Typical release steps

1. Update package `<Version>` values in csproj files.
2. Update `release/release-manifest.json`:
   - new package versions,
   - internal dependency floors,
   - publish order.
3. Run:
   - `just release-check`
4. Build release artifacts:
   - `just p`
5. Publish:
   - `just release-publish`

### Notes

- For dependency-floor-only changes (no API/behavior change), bump patch versions.
- If a package is changed and downstream packages must expose updated dependency floors, republish downstream packages with patch bumps as well.
