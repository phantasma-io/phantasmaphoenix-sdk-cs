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

// 3-4. Sign and send. Or let SendTransactionAsync do steps 2 to 4 in one call.
var hash = await api.SendTransactionAsync(transfer, keys);
```

Under gas model v2 there are two things to know, and `SendTransactionAsync` handles both for you:

1. **Plan before you sign.** The chain bills every byte the transaction puts in the block, and it
   escrows every storage row the transaction creates. A message therefore carries a gas offer and a
   storage ceiling, and both have to be set before signing. A message signed with a zero offer is
   never admitted.
2. **What the plan cannot know, it assumes expensive.** Some of the price depends on chain state the
   message does not carry. Examples are whether the recipient already holds the token, and which mode
   a series mints in. Each fact is taken at the value that costs MORE. Unused gas is refunded, and a
   short offer is rejected. `plan.Exact` says whether such an assumption decided this number.

- `api.Fees` is the fee planner of the chain the client talks to. It reads `getGasConfig` once and
  keeps it for a minute. It prices every message from the message itself. The signed size is computed
  without a key. The storage rows, call result bytes and gas sites of each native operation are
  priced with the chain's own formula (`FeePlanner.Plan`).
- `plan.Kinds` says which operations were priced. A `Call_Multi` performs several, and each one is
  priced and then summed, because the chain bills a batch as the sum of its calls with the envelope
  counted once. One kind is a budget and not a formula: `NativeFeeKind.Script`, which covers VM
  scripts and unmodelled calls. Their work depends on execution.
- `FeePlanOptions` is where you tighten point 2 by telling the planner a fact it would otherwise
  assume. Every property is optional and most callers set none.
- `plan.Exact` says whether the number is a prediction or a ceiling. It is `true` when nothing the
  plan had to assume could have changed it. It is `false` when an unstated fact decided part of the
  price, or when a part of the message had to be budgeted. A wallet shows the amount when the flag
  is `true`, and "up to" in front of the amount when it is `false`.
  The flag is answered by pricing the message a second time, with every unstated fact at its cheaper
  reading. It is therefore about THIS message and not about which properties you set. A KCAL transfer
  is exact without stating anything, because the chain's own token rows are free and
  `RecipientHoldsToken` cannot move its price. The flag promises nothing about facts you stated
  yourself: a wrong stated fact gives a wrong bill.
- `FeePlanner.BurnedInstances(msg)` names the NFT instances a message burns, in any shape. It covers
  the native burn types, a `Token.BurnNonFungible` call, and every such call inside a `Call_Multi`.
  Use it to tell whether `Infusions` is required. The caller then does not have to read the message
  itself. An empty answer means the list is not required.

### Which fact each operation reads

Only the facts an operation reads can move its price, so this table is the whole of what is worth
stating. Every default is the reading that costs MORE. One storage quantum is `dataEscrowPerRow` of
escrow plus 25 gas units of block data, and the chain's `feeMultiplier` scales both. Balance rows of
the gas and data tokens are free, so for those tokens `RecipientHoldsToken` changes nothing.

| `NativeFeeKind` | facts it reads | what the default assumes | what the default costs |
|---|---|---|---|
| `TransferFungible` | `RecipientHoldsToken` | the recipient has no row for this token | 1 quantum |
| `TransferNonFungible` | `RecipientHoldsToken` | the recipient has no row for this token | 1 quantum |
| `MintFungible` | `RecipientHoldsToken`, `SupplyRowExists`, `BigFungible` | no recipient row; the supply row was dropped and must be recreated; the resulting balance needs the widest answer | 1 quantum each, and 24 more result bytes (33 against 9) |
| `BurnFungible` | `TokenBurnedBefore`, `SupplyRowExists`, `BigFungible` | the token's burnt counter does not exist yet; the supply row must be recreated; widest answer | 1 quantum each, and 24 more result bytes |
| `MintNonFungible` | `RecipientHoldsToken`, `SupplyRowExists`, `RomHasMetaId` | as above, plus: the ROM carries an `_i` id, which is indexed in one more row | 1 quantum each, and 1 quantum per instance for the id |
| `MintPhantasmaNonFungible` | `RecipientHoldsToken`, `SupplyRowExists`, `DuplicatedSeries` | as above, plus: the series mints duplicates | 1 quantum each, and one query fee per instance plus one per distinct series |
| `BurnNonFungible` | `TokenBurnedBefore`, `SupplyRowExists`, **`Infusions` (required)** | burnt counter does not exist; supply row must be recreated | 1 quantum each. `Infusions` has no default at all. A burn returns whatever the NFT holds, and there is no upper bound on that, so the plan demands the list and `api.Fees` reads it from the chain. `RomHasMetaId` is read here too but cannot move the bill: a burn deletes more rows than it creates, so its net storage growth is zero either way |
| `CreateToken` | none | nothing is assumed | the price comes entirely from the message: the symbol length, the serialized `TokenInfo`, and which keys its metadata carries |
| `CreateTokenSeries` | `SeriesHasMetaId` | the series metadata carries an `_i` id | 1 quantum |
| `RegisterName` | none | nothing is assumed | governance rows are free data; the price is the length-shifted policy fee and the envelope |
| `Script` | none | 5000 work units, 512 event bytes and 4 storage quanta, per unmodelled call | a budget and never a prediction. `plan.Exact` is `false` whenever one is present |

- A message the caller has already planned (`maxGas > 0`) is sent as it is. Signing an unplanned
  message is refused, because a zero offer is never admitted.
- `SendTransactionAsync` runs a pre-flight before signing a **token creation**. `Token.CreateToken`
  is charged its policy fee **before** the contract looks at the symbol. That fee is the largest
  single price in the protocol. Governance sets it and `getGasConfig` reports it. Sending a symbol
  that is already taken pays the fee and gets nothing back. One lookup avoids that
  (`TransactionPreflight.PreflightAsync`). Every other message goes straight through.
- The pre-flight never reads a verdict out of an error message. A symbol that resolves is `Taken`. A
  symbol that does not resolve comes back as an ordinary RPC error, and a broken or proxied node
  produces that same error just as readily. So the check asks a second question whose answer it
  already knows. It fetches the gas token by its id, and that lookup does not touch symbols. When the
  node answers it, the node is serving token lookups and the symbol is `Free`. When the node does not
  answer it, the verdict is `Unknown` and nothing was established. `SendTransactionAsync` refuses on
  `Taken` and on `Unknown`. A caller that wants to decide for itself calls
  `TransactionPreflight.PreflightAsync`, reads the verdict and sends with
  `new SendTransactionOptions { Preflight = false }`. It can then warn before the fee is spent, or
  try another node.
- A burn returns whatever the NFT holds at its own address, and the chain charges for each
  returned asset: a transfer fee and an owner lookup per fungible token, an instance query, a
  transfer per instance and that lookup per NFT token, plus a balance row for a returned token the
  burner does not hold. That set is chain state with no costlier bound, so `FeePlanner.Plan`
  demands `FeePlanOptions.Infusions` for a burn. An empty list says the NFT holds nothing.
  `api.Fees.PlanAsync`, and so `SendTransactionAsync`, reads the list from the chain
  (`PhantasmaAPI.InfusedAssetsAsync`).
- Any witness that implements `ITxSigner` (`PublicKey` + `SignAsync(bytes)`) can sign, and
  `TxMsgSigner.SignWithAsync` accepts several. The gas-payer transaction types take two. A wallet
  that signs elsewhere plans with `api.Fees.PlanAsync` and hands `plan.Apply(msg)` over.
- A contract call (`Call`, `Call_Multi`, `Trade`, `Phantasma`) chooses its own witnesses, and each
  one is 96 bytes the chain bills. So `FeePlanner.Plan` asks for `WitnessCount`. An assumed single
  witness would under-offer every multi-party transaction by 96 bytes per extra signature.
  `SendTransactionAsync` fills the count in from the signers it was given. The gas payer must be one
  of them, because the chain rejects a transaction its payer did not sign.
- A message expires. Builders stamp `TxLimits.DefaultExpiryMs` from now, which is sized for a
  transaction signed on the spot. When a person has to approve it first, take the chain's whole
  window with `TxLimits.ExpiryWithin((await api.Fees.ChainParamsAsync()).ExpiryWindow)`.
- `UnitConversion.ToDecimalString` and `UnitConversion.ToBigInteger` convert between atoms and
  decimal amounts. KCAL has 10 decimals and SOUL has 8. `FeePlanSummary.Summarize` renders a plan in
  KCAL and SOUL for display.

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
