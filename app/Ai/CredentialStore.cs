using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VNdev.App.Ai;

/// <summary>
/// Cất khoá API vào Windows Credential Manager — chỗ Windows dành riêng cho
/// mật khẩu, được mã hoá theo tài khoản đăng nhập.
/// </summary>
/// <remarks>
/// Không để trong settings.json hay thư mục dự án: file cài đặt là văn bản
/// thường, còn thư mục dự án thì người dùng đưa lên Git — một lần commit nhầm
/// là khoá lộ công khai và bị người lạ dùng tốn tiền. Gọi thẳng advapi32 thay
/// vì kéo thêm thư viện, vì chỉ cần ba hàm đọc/ghi/xoá.
/// </remarks>
public static class CredentialStore
{
    private const string Prefix = "VNdev/ai/";
    private const int CredTypeGeneric = 1;
    private const int PersistLocalMachine = 2;

    // Trên máy không phải Windows (chưa hỗ trợ chính thức) khoá chỉ sống trong
    // phiên chạy — thà hỏi lại mỗi lần mở app còn hơn ghi ra file thường.
    private static readonly System.Collections.Generic.Dictionary<string, string> Fallback = new();

    public static string? Get(string providerId)
    {
        var target = Prefix + providerId;
        if (!OperatingSystem.IsWindows()) return Fallback.TryGetValue(target, out var v) ? v : null;

        if (!CredRead(target, CredTypeGeneric, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static bool Has(string providerId) => !string.IsNullOrEmpty(Get(providerId));

    public static void Set(string providerId, string secret)
    {
        var target = Prefix + providerId;
        if (!OperatingSystem.IsWindows())
        {
            Fallback[target] = secret;
            return;
        }

        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "VNdev",
            };
            if (!CredWrite(ref cred, 0))
            {
                throw new InvalidOperationException($"Windows không cho lưu khoá (mã lỗi {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public static void Delete(string providerId)
    {
        var target = Prefix + providerId;
        if (!OperatingSystem.IsWindows())
        {
            Fallback.Remove(target);
            return;
        }
        CredDelete(target, CredTypeGeneric, 0);
    }

    /// <summary>Hiện khoá dạng "sk-ant-••••4f2a" như bản thiết kế — đủ để nhận ra, không đủ để chép.</summary>
    public static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 10) return "••••";
        var dash = key.IndexOf('-', 3);
        var head = dash > 0 && dash < 10 ? key[..(dash + 1)] : key[..4];
        return $"{head}••••{key[^4..]}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
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
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
