using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Sinedu.SharedKernel.Storage;

public interface IObjectStorage
{
    Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default);

    Task DeleteBucketAsync(string bucket, CancellationToken cancellationToken = default);

    Task PutAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Conteúdo do objeto, ou null se não existir.</summary>
    Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default);
}

public sealed class S3Options
{
    public const string Section = "Storage";

    public Uri ServiceUrl { get; set; } = new("http://localhost:8333");

    public string AccessKey { get; set; } = "";

    public string SecretKey { get; set; } = "";
}

/// <summary>Armazenamento S3-compatível (SeaweedFS em dev). Um bucket por escola.</summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;

    public S3ObjectStorage(IOptions<S3Options> options)
    {
        var o = options.Value;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(o.AccessKey, o.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl.ToString(),
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",

                // O SDK v4 envia o corpo em "aws-chunked" com checksum no trailer por padrão; servidores S3-compatíveis
                // (SeaweedFS) podem gravar o envelope junto com o arquivo. Só checksums exigidos pela operação.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var existing = await _client.ListBucketsAsync(cancellationToken);
        if (existing.Buckets?.Any(b => b.BucketName == bucket) == true)
        {
            return;
        }

        await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
    }

    public async Task DeleteBucketAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var objects = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, cancellationToken);
        foreach (var obj in objects.S3Objects ?? [])
        {
            await _client.DeleteObjectAsync(bucket, obj.Key, cancellationToken);
        }

        await _client.DeleteBucketAsync(bucket, cancellationToken);
    }

    public async Task PutAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content, writable: false);
        await _client.PutObjectAsync(
            new PutObjectRequest { BucketName = bucket, Key = key, InputStream = stream, ContentType = "application/octet-stream", AutoCloseStream = false, UseChunkEncoding = false },
            cancellationToken);
    }

    public async Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(bucket, key, cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
