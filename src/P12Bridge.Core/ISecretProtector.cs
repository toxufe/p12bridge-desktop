namespace P12Bridge.Core;

public interface ISecretProtector
{
    string Protect(string value);

    string Unprotect(string protectedValue);
}
