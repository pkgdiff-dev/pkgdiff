using PkgDiff.Domain.Common;

namespace PkgDiff.Domain.Entities;

public class RequestLog : BaseEntity
{
    public string IpAddress { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}
