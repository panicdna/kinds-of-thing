$ErrorActionPreference = 'Stop'
$src = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class CredTest
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        // wincred.h 순서: CredentialBlobSize(DWORD)가 CredentialBlob(LPBYTE)보다 먼저다.
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    public static string RoundTrip(string target, string secret)
    {
        byte[] blob = Encoding.Unicode.GetBytes(secret);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        Marshal.Copy(blob, 0, blobPtr, blob.Length);
        IntPtr targetPtr = Marshal.StringToHGlobalUni(target);
        IntPtr userPtr = Marshal.StringToHGlobalUni(Environment.UserName);
        try
        {
            var c = new NativeCredential();
            c.Type = 1;
            c.TargetName = targetPtr;
            c.UserName = userPtr;
            c.CredentialBlobSize = blob.Length;
            c.CredentialBlob = blobPtr;
            c.Persist = 2;
            if (!CredWrite(ref c, 0)) return "CredWrite failed: " + Marshal.GetLastWin32Error();
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr); Marshal.FreeHGlobal(targetPtr); Marshal.FreeHGlobal(userPtr);
        }
        IntPtr ptr;
        if (!CredRead(target, 1, 0, out ptr)) return "CredRead failed: " + Marshal.GetLastWin32Error();
        try
        {
            var c = (NativeCredential)Marshal.PtrToStructure(ptr, typeof(NativeCredential));
            byte[] read = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, read, 0, read.Length);
            string back = Encoding.Unicode.GetString(read);
            if (back != secret) return "MISMATCH: '" + back + "'";
        }
        finally { CredFree(ptr); }
        CredDelete(target, 1, 0);
        return "ROUNDTRIP OK";
    }
}
'@
Add-Type -TypeDefinition $src
[CredTest]::RoundTrip('DeskHooker.KnoxMail.SelfTest', 'test-secret-123')
