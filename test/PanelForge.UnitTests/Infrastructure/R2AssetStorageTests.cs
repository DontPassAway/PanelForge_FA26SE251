using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Moq;
using PanelForge.Application.Common.Interfaces.Services;
using PanelForge.Infrastructure.Services;

namespace PanelForge.UnitTests.Infrastructure;

public class R2AssetStorageTests
{
    private const string Bucket = "panelforge-test";
    private const string Key = "ws/ab/abc";

    private static AmazonS3Exception NotFound() => new("not found") { StatusCode = HttpStatusCode.NotFound };

    [Fact]
    public async Task Save_NewKey_PutsObjectWithoutPayloadSigning()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(Bucket, Key, It.IsAny<CancellationToken>())).ThrowsAsync(NotFound());
        PutObjectRequest? sent = null;
        byte[]? body = null;
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => { sent = r; body = ((MemoryStream)r.InputStream).ToArray(); })
            .ReturnsAsync(new PutObjectResponse());

        await new R2AssetStorage(s3.Object, Bucket).SaveAsync(Key, [1, 2, 3], new AssetObjectInfo("image/png", new Dictionary<string, string> { ["workspace"] = "Studio Đêm Trăng", ["page"] = "1", ["layer"] = "" }));

        Assert.NotNull(sent);
        Assert.Equal(Bucket, sent!.BucketName);
        Assert.Equal(Key, sent.Key);
        Assert.True(sent.DisablePayloadSigning);
        Assert.Equal(new byte[] { 1, 2, 3 }, body);
        Assert.Equal("image/png", sent.Headers.ContentType);
        Assert.Equal("Studio Dem Trang", sent.Metadata["workspace"]); // header S3 chỉ ASCII → bỏ dấu
        Assert.Equal("1", sent.Metadata["page"]);
        Assert.DoesNotContain("x-amz-meta-layer", sent.Metadata.Keys); // giá trị rỗng không ghi
    }

    [Theory]
    [InlineData("Lantern of the Tide", "Lantern of the Tide")]
    [InlineData("Đèn biển – Chương 1", "Den bien ? Chuong 1")]
    public void ToHeaderValue_FoldsToAscii(string input, string expected)
        => Assert.Equal(expected, R2AssetStorage.ToHeaderValue(input));

    [Fact]
    public async Task Save_ExistingKey_DoesNotUploadAgain()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectMetadataAsync(Bucket, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        await new R2AssetStorage(s3.Object, Bucket).SaveAsync(Key, [1], info: null);

        s3.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenRead_Missing_ReturnsNull()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(Bucket, Key, It.IsAny<CancellationToken>())).ThrowsAsync(NotFound());

        Assert.Null(await new R2AssetStorage(s3.Object, Bucket).OpenReadAsync(Key));
    }

    [Fact]
    public async Task OpenRead_Existing_ReturnsStream()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(Bucket, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([7, 8]) });

        await using var stream = await new R2AssetStorage(s3.Object, Bucket).OpenReadAsync(Key);
        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        Assert.Equal(new byte[] { 7, 8 }, copy.ToArray());
    }

    [Fact]
    public void FromConfiguration_MissingSecret_FailsWithClearMessage()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssetStorage:R2:AccountId"] = "acc",
            ["AssetStorage:R2:Bucket"] = Bucket,
            ["AssetStorage:R2:AccessKeyId"] = "id"
        }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => R2AssetStorage.FromConfiguration(config));
        Assert.Contains("AssetStorage:R2:SecretAccessKey", ex.Message);
    }
}
