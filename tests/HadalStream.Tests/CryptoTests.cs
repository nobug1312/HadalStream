using System.Text;
using System.Text.Json;
using HadalStream.Infrastructure.Sources;

namespace HadalStream.Tests;

public class CryptoTests
{
    [Fact]
    public void Md5Hex_String_IsStandardMd5() =>
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", AbyssCrypto.Md5Hex("abc"));

    // Expected value produced by the original keyGenerator.js with a numeric argument.
    [Fact]
    public void Md5Hex_Number_HashesDigitValues() =>
        Assert.Equal("498001217bc632cb158588224d7d23c4", AbyssCrypto.Md5Hex(1234567L));

    // NIST SP 800-38A F.5.5 CTR-AES256.Encrypt (also exercises counter carry fe ff -> ff 00).
    [Fact]
    public void AesCtr_MatchesNistVector()
    {
        var key = Convert.FromHexString("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
        var iv = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var plain = Convert.FromHexString(
            "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710");
        var expected = "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6";

        Assert.Equal(expected, Convert.ToHexStringLower(AbyssCrypto.AesCtr(plain, key, iv)));
    }

    // Expected value produced by Node's OpenSSL aes-256-ctr with the same key/IV derivation.
    [Fact]
    public void SegmentToken_MatchesIndependentImplementation() =>
        Assert.Equal("VElnZ2xoQkRxZ09QMUxSQTFiaXhPU0xPMGU4V3lKbmtxcTJV", AbyssCrypto.SegmentToken("/mp4/42/3/1234567/2097152/7", 1234567));

    [Fact]
    public void AbyssParse_DecodesDatasPayload()
    {
        var mediaJson = """{"mp4":{"domains":["cdn.example.net"],"sources":[{"label":"720p","size":5000000,"res_id":3,"sub":"s12"},{"label":"720p","size":4000000,"res_id":4,"sub":"s13"}]}}""";
        var encrypted = AbyssCrypto.AesCtr(Encoding.UTF8.GetBytes(mediaJson), AbyssCrypto.Md5Hex("7:abc:42"));
        var datas = JsonSerializer.Serialize(new { user_id = 7, slug = "abc", md5_id = 42, media = Encoding.Latin1.GetString(encrypted) });
        var html = $"<title>My Video.mp4</title><script>const datas = \"{Convert.ToBase64String(Encoding.Latin1.GetBytes(datas)).TrimEnd('=')}\";</script>";

        var media = AbyssSource.Parse(html);

        Assert.Equal("My Video", media.Title);
        Assert.Equal("42", media.Md5Id);
        Assert.Equal("cdn.example.net", media.Domain);
        Assert.Equal(
            new[] { new AbyssSource.Source("720p", 5000000, "3", "s12"), new AbyssSource.Source("720p (2)", 4000000, "4", "s13") },
            media.Sources);
    }
}
