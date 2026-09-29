using LiteFactoryLauncher.Models;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LiteFactoryLauncher.Services;

public sealed class CredentialStorageService
{
    private const string TargetName = "LiteFactoryLauncher:LiteFactoryLogin";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistenceLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public RememberedCredentials? Load()
    {
        if (!CredRead(TargetName, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            var login = credential.UserName ?? "";
            if (string.IsNullOrWhiteSpace(login) || credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                Delete();
                return null;
            }

            var passwordBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, passwordBytes.Length);
            var password = Encoding.Unicode.GetString(passwordBytes).TrimEnd('\0');
            if (string.IsNullOrEmpty(password))
            {
                Delete();
                return null;
            }

            return new RememberedCredentials
            {
                Login = login,
                Password = password
            };
        }
        catch
        {
            Delete();
            return null;
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Save(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
        {
            Delete();
            return;
        }

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var credential = new NativeCredential
        {
            Type = CredentialTypeGeneric,
            TargetName = TargetName,
            CredentialBlobSize = passwordBytes.Length,
            Persist = CredentialPersistenceLocalMachine,
            UserName = login
        };

        var blobPointer = Marshal.AllocCoTaskMem(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, blobPointer, passwordBytes.Length);
            credential.CredentialBlob = blobPointer;

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            Marshal.FreeCoTaskMem(blobPointer);
        }
    }

    public void Delete()
    {
        if (CredDelete(TargetName, CredentialTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
