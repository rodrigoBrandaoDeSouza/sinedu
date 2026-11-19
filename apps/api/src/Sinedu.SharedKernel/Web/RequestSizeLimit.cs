namespace Sinedu.SharedKernel.Web;

/// <summary>Limite de tamanho do corpo por endpoint (minimal APIs).</summary>
public sealed class MaxRequestSize(long bytes) : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize => bytes;
}
