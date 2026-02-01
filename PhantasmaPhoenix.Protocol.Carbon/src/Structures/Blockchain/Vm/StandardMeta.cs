namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public static class StandardMeta
{
	public static SmallString id = new("_i");

	public static class Chain
	{
		public static SmallString address = new("_a");
		public static SmallString name = new("_n");
		public static SmallString nexus = new("_x");
		public static SmallString tokenomics = new("_t");
	}

	public static class Token
	{
		public static SmallString staking_org_id = new("_soi");
		public static SmallString staking_org_threshold = new("_sot");
		public static SmallString staking_reward_token = new("_srt");
		public static SmallString staking_reward_period = new("_srp");
		public static SmallString staking_reward_mul = new("_srm");
		public static SmallString staking_reward_div = new("_srd");
		public static SmallString staking_lock = new("_sl");
		public static SmallString staking_booster_token = new("_sbt");
		public static SmallString staking_booster_mul = new("_sbm");
		public static SmallString staking_booster_div = new("_sbd");
		public static SmallString staking_booster_limit = new("_sbl");
		public static SmallString phantasma_script = new("_phs");
		public static SmallString phantasma_abi = new("_phb");
		public static SmallString pre_burn = new("_brn");

		public static class Nft
		{
		public static SmallString royalties = new("royalties");
		public static SmallString phantasmaRom = new("rom");
	}
}
}
