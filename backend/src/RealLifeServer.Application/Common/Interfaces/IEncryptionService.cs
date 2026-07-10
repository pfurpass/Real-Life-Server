namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>AES-256-GCM encryption for secrets at rest (destination stream keys). See docs/CONCEPT.md, chapter 10.</summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
