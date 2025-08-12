using System.ComponentModel.DataAnnotations;

namespace FixedRatioStressTest.Common.Models;

public class SolanaConfiguration
{
    [Required]
    public string RpcUrl { get; set; } = "https://api.devnet.solana.com";
    
    [Required]
    public string Network { get; set; } = "devnet";
    
    [Required]
    public string Commitment { get; set; } = "confirmed";
}
