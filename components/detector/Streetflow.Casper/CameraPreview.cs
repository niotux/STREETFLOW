namespace Streetflow.Casper;

public sealed record CameraPreview(byte[] Image, DateTimeOffset ObservedAt, string Revision, string Status);
