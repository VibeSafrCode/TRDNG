using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Trdng.Core.Instruments;

namespace Trdng.Core.Credentials;

public sealed class MacOsKeychainCredentialVault : ICredentialVault
{
    private const string Service = "com.trdng.desktop.credentials.v1";
    private const int Success = 0;
    private const int DuplicateItem = -25299;
    private const int ItemNotFound = -25300;
    private const int AuthFailed = -25293;
    private const int InteractionNotAllowed = -25308;
    private const int InvalidParameter = -50;
    private const int MissingEntitlement = -34018;
    // Local defensive boundary; exchange credentials are far smaller than 16 KiB.
    public const int MaxSecretBytes = 16 * 1024;
    private static readonly Lazy<NativeSymbols> Symbols = new(
        NativeSymbols.Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public ValueTask<CredentialVaultResult> StoreAsync(
        CredentialIdentity identity, ReadOnlyMemory<byte> secret, bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return ValueTask.FromResult(Unavailable());
        if (!CredentialIdentityPolicy.IsValid(identity))
            return ValueTask.FromResult(InvalidIdentity());
        if (secret.IsEmpty || secret.Length > MaxSecretBytes) return ValueTask.FromResult(
            new CredentialVaultResult(CredentialVaultState.Denied, "SECRET SIZE DENIED"));

        var copy = secret.ToArray();
        try
        {
            using var query = Query(identity, includeReturnData: false);
            using var value = OwnedCf.Data(copy);
            int status;
            if (overwrite)
            {
                using var attributes = OwnedCf.Dictionary(
                    [(Symbols.Value.SecValueData, value.Handle)]);
                status = SecItemUpdate(query.Handle, attributes.Handle);
                if (status == ItemNotFound)
                {
                    using var add = AddQuery(identity, value.Handle);
                    status = SecItemAdd(add.Handle, out var added);
                    if (added != IntPtr.Zero) CFRelease(added);
                }
            }
            else
            {
                using var add = AddQuery(identity, value.Handle);
                status = SecItemAdd(add.Handle, out var added);
                if (added != IntPtr.Zero) CFRelease(added);
            }
            return ValueTask.FromResult(Map(status,
                status == DuplicateItem ? "OVERWRITE REQUIRED" : "STORE"));
        }
        catch (Exception exception) when (IsNativeBoundaryFailure(exception))
        {
            return ValueTask.FromResult(Error(exception));
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    public ValueTask<CredentialReadResult> ReadAsync(
        CredentialIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return ValueTask.FromResult(
            new CredentialReadResult(CredentialVaultState.Unavailable, null,
                "MACOS KEYCHAIN UNAVAILABLE", CredentialFailureCode.UnsupportedPlatform));
        if (!CredentialIdentityPolicy.IsValid(identity)) return ValueTask.FromResult(
            new CredentialReadResult(CredentialVaultState.Denied, null,
                "INVALID CREDENTIAL IDENTITY", CredentialFailureCode.InvalidIdentity));
        try
        {
            using var query = Query(identity, includeReturnData: true);
            var status = SecItemCopyMatching(query.Handle, out var result);
            if (status != Success)
            {
                if (result != IntPtr.Zero) CFRelease(result);
                var mapped = Map(status, "READ");
                return ValueTask.FromResult(
                    new CredentialReadResult(mapped.State, null, mapped.Reason,
                        mapped.FailureCode, mapped.DiagnosticStage));
            }
            try
            {
                if (result == IntPtr.Zero)
                    return ValueTask.FromResult(new CredentialReadResult(
                        CredentialVaultState.Error, null, "KEYCHAIN READ INVALID RESULT",
                        CredentialFailureCode.InvalidParameter, CredentialDiagnosticStage.SecItem));
                var length = checked((int)CFDataGetLength(result));
                if (length <= 0 || length > MaxSecretBytes)
                    return ValueTask.FromResult(new CredentialReadResult(
                        CredentialVaultState.Error, null, "KEYCHAIN READ SIZE DENIED",
                        CredentialFailureCode.InvalidParameter, CredentialDiagnosticStage.SecItem));
                var pointer = CFDataGetBytePtr(result);
                if (pointer == IntPtr.Zero)
                    return ValueTask.FromResult(new CredentialReadResult(
                        CredentialVaultState.Error, null, "KEYCHAIN READ INVALID DATA",
                        CredentialFailureCode.InvalidParameter, CredentialDiagnosticStage.SecItem));
                var bytes = new byte[length];
                Marshal.Copy(pointer, bytes, 0, length);
                return ValueTask.FromResult(new CredentialReadResult(
                    CredentialVaultState.Stored, new SecretLease(bytes), "STORED",
                    CredentialFailureCode.None, CredentialDiagnosticStage.SecItem));
            }
            finally { if (result != IntPtr.Zero) CFRelease(result); }
        }
        catch (Exception exception) when (IsNativeBoundaryFailure(exception))
        {
            var error = Error(exception);
            return ValueTask.FromResult(new CredentialReadResult(error.State, null, error.Reason,
                error.FailureCode, error.DiagnosticStage));
        }
    }

    public ValueTask<CredentialVaultResult> RevokeAsync(
        CredentialIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return ValueTask.FromResult(Unavailable());
        if (!CredentialIdentityPolicy.IsValid(identity))
            return ValueTask.FromResult(InvalidIdentity());
        try
        {
            using var query = Query(identity, includeReturnData: false);
            var status = SecItemDelete(query.Handle);
            return ValueTask.FromResult(MapRevokeStatus(status));
        }
        catch (Exception exception) when (IsNativeBoundaryFailure(exception))
        {
            return ValueTask.FromResult(Error(exception));
        }
    }

    public ValueTask<CredentialVaultResult> GetStatusAsync(
        CredentialIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return ValueTask.FromResult(Unavailable());
        if (!CredentialIdentityPolicy.IsValid(identity))
            return ValueTask.FromResult(InvalidIdentity());
        try
        {
            using var query = Query(identity, includeReturnData: false);
            var status = SecItemCopyMatching(query.Handle, out var result);
            if (result != IntPtr.Zero) CFRelease(result);
            return ValueTask.FromResult(Map(status, "STATUS"));
        }
        catch (Exception exception) when (IsNativeBoundaryFailure(exception))
        {
            return ValueTask.FromResult(Error(exception));
        }
    }

    private static OwnedCf Query(CredentialIdentity identity, bool includeReturnData)
    {
        using var service = OwnedCf.String(Service);
        using var account = OwnedCf.String(identity.Account);
        var entries = new List<(IntPtr, IntPtr)>
        {
            (Symbols.Value.SecClass, Symbols.Value.SecClassGenericPassword),
            (Symbols.Value.SecAttrService, service.Handle),
            (Symbols.Value.SecAttrAccount, account.Handle)
        };
        if (includeReturnData)
        {
            entries.Add((Symbols.Value.SecReturnData, Symbols.Value.CfBooleanTrue));
            entries.Add((Symbols.Value.SecMatchLimit, Symbols.Value.SecMatchLimitOne));
        }
        return OwnedCf.Dictionary(entries);
    }

    private static OwnedCf AddQuery(CredentialIdentity identity, IntPtr data)
    {
        using var service = OwnedCf.String(Service);
        using var account = OwnedCf.String(identity.Account);
        return OwnedCf.Dictionary(
        [
            (Symbols.Value.SecClass, Symbols.Value.SecClassGenericPassword),
            (Symbols.Value.SecAttrService, service.Handle),
            (Symbols.Value.SecAttrAccount, account.Handle),
            (Symbols.Value.SecValueData, data)
        ]);
    }

    public static CredentialVaultResult MapRevokeStatus(int status) => status switch
    {
        Success => new(CredentialVaultState.NotConfigured, "NOT CONFIGURED",
            CredentialFailureCode.None, CredentialDiagnosticStage.SecItem),
        ItemNotFound => new(CredentialVaultState.NotConfigured, "NOT CONFIGURED",
            CredentialFailureCode.ItemNotFound, CredentialDiagnosticStage.SecItem),
        _ => Map(status, "REVOKE")
    };

    private static CredentialVaultResult Map(int status, string operation) => status switch
    {
        Success => new(CredentialVaultState.Stored, $"{operation} OK",
            CredentialFailureCode.None, CredentialDiagnosticStage.SecItem),
        ItemNotFound => new(CredentialVaultState.NotConfigured, "NOT CONFIGURED",
            CredentialFailureCode.ItemNotFound, CredentialDiagnosticStage.SecItem),
        DuplicateItem => new(CredentialVaultState.Denied, "OVERWRITE REQUIRED",
            CredentialFailureCode.DuplicateItem, CredentialDiagnosticStage.SecItem),
        AuthFailed => new(CredentialVaultState.Denied, "KEYCHAIN ACCESS DENIED",
            CredentialFailureCode.AuthenticationFailed, CredentialDiagnosticStage.SecItem),
        InteractionNotAllowed => new(CredentialVaultState.Unavailable,
            "UNLOCKED USER SESSION REQUIRED", CredentialFailureCode.InteractionNotAllowed,
            CredentialDiagnosticStage.SecItem),
        MissingEntitlement => new(CredentialVaultState.Denied, "KEYCHAIN ACCESS DENIED",
            CredentialFailureCode.MissingEntitlement, CredentialDiagnosticStage.SecItem),
        InvalidParameter => new(CredentialVaultState.Error, "KEYCHAIN PARAMETER ERROR",
            CredentialFailureCode.InvalidParameter, CredentialDiagnosticStage.SecItem),
        _ => new(CredentialVaultState.Error, "KEYCHAIN OPERATION FAILED",
            CredentialFailureCode.OtherOsStatus, CredentialDiagnosticStage.SecItem)
    };

    private static CredentialVaultResult Unavailable() =>
        new(CredentialVaultState.Unavailable, "MACOS KEYCHAIN UNAVAILABLE",
            CredentialFailureCode.UnsupportedPlatform);
    private static CredentialVaultResult InvalidIdentity() =>
        new(CredentialVaultState.Denied, "INVALID CREDENTIAL IDENTITY",
            CredentialFailureCode.InvalidIdentity);
    private static CredentialVaultResult Error(Exception exception) => exception is CredentialInteropException interop
        ? new(CredentialVaultState.Error, "KEYCHAIN NATIVE BOUNDARY ERROR",
            interop.Code, interop.Stage)
        : new(CredentialVaultState.Error, "KEYCHAIN NATIVE BOUNDARY ERROR",
            CredentialFailureCode.UnexpectedBoundaryFailure);
    private static bool IsNativeBoundaryFailure(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or ExternalException or OverflowException or
            InvalidOperationException or TypeInitializationException or CredentialInteropException;

    public static CredentialInteropDiagnostic RunInteropDiagnostic()
    {
        if (!OperatingSystem.IsMacOS()) return new(false,
            CredentialDiagnosticStage.NativeLibrary, CredentialFailureCode.UnsupportedPlatform);
        try
        {
            _ = Symbols.Value.SecClass;
            using var text = OwnedCf.String("trdng-interop-diagnostic");
            if (text.Handle == IntPtr.Zero) return new(false,
                CredentialDiagnosticStage.CoreFoundationString,
                CredentialFailureCode.CoreFoundationStringFailure);
            using var data = OwnedCf.Data([1, 2, 3, 4]);
            if (data.Handle == IntPtr.Zero || CFDataGetLength(data.Handle) != 4 ||
                CFDataGetBytePtr(data.Handle) == IntPtr.Zero) return new(false,
                CredentialDiagnosticStage.CoreFoundationData,
                CredentialFailureCode.CoreFoundationDataFailure);
            using var dictionary = OwnedCf.Dictionary(
                [(Symbols.Value.SecClass, Symbols.Value.SecClassGenericPassword),
                 (Symbols.Value.SecAttrService, text.Handle),
                 (Symbols.Value.SecValueData, data.Handle)]);
            if (dictionary.Handle == IntPtr.Zero) return new(false,
                CredentialDiagnosticStage.CoreFoundationDictionary,
                CredentialFailureCode.CoreFoundationDictionaryFailure);
            return new(true, CredentialDiagnosticStage.None, CredentialFailureCode.None);
        }
        catch (CredentialInteropException exception)
        {
            return new(false, exception.Stage, exception.Code);
        }
        catch
        {
            return new(false, CredentialDiagnosticStage.NativeSymbols,
                CredentialFailureCode.UnexpectedBoundaryFailure);
        }
    }

    public sealed record CredentialInteropDiagnostic(bool Success,
        CredentialDiagnosticStage Stage, CredentialFailureCode Code);

    public static CredentialDictionaryDiagnostic RunDictionaryDiagnostic()
    {
        if (!OperatingSystem.IsMacOS())
            return new(false, "UNSUPPORTED_PLATFORM");
        try
        {
            var identity = new CredentialIdentity(TradingVenue.Mexc, "dictionary-diagnostic");
            using var statusQuery = Query(identity, includeReturnData: false);
            if (!ValidateDictionary(statusQuery.Handle, 3,
                [(Symbols.Value.SecClass, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrService, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrAccount, CFStringGetTypeID())]))
                return new(false, "STATUS_QUERY_SHAPE");

            using var readQuery = Query(identity, includeReturnData: true);
            if (!ValidateDictionary(readQuery.Handle, 5,
                [(Symbols.Value.SecClass, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrService, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrAccount, CFStringGetTypeID()),
                 (Symbols.Value.SecReturnData, CFBooleanGetTypeID()),
                 (Symbols.Value.SecMatchLimit, CFStringGetTypeID())]))
                return new(false, "READ_QUERY_SHAPE");

            using var addQuery = CreateRetainedDiagnosticAddQuery(identity);
            if (!ValidateDictionary(addQuery.Handle, 4,
                [(Symbols.Value.SecClass, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrService, CFStringGetTypeID()),
                 (Symbols.Value.SecAttrAccount, CFStringGetTypeID()),
                 (Symbols.Value.SecValueData, CFDataGetTypeID())]))
                return new(false, "ADD_QUERY_SHAPE");

            return new(true, "PASS");
        }
        catch
        {
            return new(false, "BOUNDARY_FAILURE");
        }
    }

    private static OwnedCf CreateRetainedDiagnosticAddQuery(CredentialIdentity identity)
    {
        using var data = OwnedCf.Data([1, 2, 3, 4]);
        return AddQuery(identity, data.Handle);
    }

    private static bool ValidateDictionary(IntPtr dictionary, nint expectedCount,
        IReadOnlyList<(IntPtr Key, nuint Type)> expected)
    {
        if (dictionary == IntPtr.Zero || CFGetTypeID(dictionary) != CFDictionaryGetTypeID() ||
            CFDictionaryGetCount(dictionary) != expectedCount || expected.Count != expectedCount)
            return false;
        foreach (var entry in expected)
        {
            var value = CFDictionaryGetValue(dictionary, entry.Key);
            if (value == IntPtr.Zero || CFGetTypeID(value) != entry.Type) return false;
        }
        return true;
    }

    public sealed record CredentialDictionaryDiagnostic(bool Success, string Code);

    private sealed class CredentialInteropException(
        CredentialDiagnosticStage stage, CredentialFailureCode code, Exception inner)
        : Exception("Native credential interop failure", inner)
    {
        public CredentialDiagnosticStage Stage { get; } = stage;
        public CredentialFailureCode Code { get; } = code;
    }

    private sealed class NativeSymbols
    {
        private const string SecurityPath =
            "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundationPath =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        public required IntPtr SecClass { get; init; }
        public required IntPtr SecClassGenericPassword { get; init; }
        public required IntPtr SecAttrService { get; init; }
        public required IntPtr SecAttrAccount { get; init; }
        public required IntPtr SecValueData { get; init; }
        public required IntPtr SecReturnData { get; init; }
        public required IntPtr SecMatchLimit { get; init; }
        public required IntPtr SecMatchLimitOne { get; init; }
        public required IntPtr CfBooleanTrue { get; init; }
        public required IntPtr DictionaryKeyCallbacks { get; init; }
        public required IntPtr DictionaryValueCallbacks { get; init; }

        public static NativeSymbols Load()
        {
            // Frameworks remain loaded for process lifetime; one load avoids loader-ref growth.
            IntPtr security;
            IntPtr coreFoundation;
            try
            {
                security = NativeLibrary.Load(SecurityPath);
                coreFoundation = NativeLibrary.Load(CoreFoundationPath);
            }
            catch (Exception exception)
            {
                throw new CredentialInteropException(CredentialDiagnosticStage.NativeLibrary,
                    CredentialFailureCode.NativeLibraryFailure, exception);
            }
            IntPtr Constant(IntPtr handle, string name) =>
                Marshal.ReadIntPtr(NativeLibrary.GetExport(handle, name));
            IntPtr Symbol(IntPtr handle, string name) => NativeLibrary.GetExport(handle, name);
            try { return new NativeSymbols
            {
                SecClass = Constant(security, "kSecClass"),
                SecClassGenericPassword = Constant(security, "kSecClassGenericPassword"),
                SecAttrService = Constant(security, "kSecAttrService"),
                SecAttrAccount = Constant(security, "kSecAttrAccount"),
                SecValueData = Constant(security, "kSecValueData"),
                SecReturnData = Constant(security, "kSecReturnData"),
                SecMatchLimit = Constant(security, "kSecMatchLimit"),
                SecMatchLimitOne = Constant(security, "kSecMatchLimitOne"),
                CfBooleanTrue = Constant(coreFoundation, "kCFBooleanTrue"),
                DictionaryKeyCallbacks = Symbol(coreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                DictionaryValueCallbacks = Symbol(coreFoundation, "kCFTypeDictionaryValueCallBacks")
            }; }
            catch (Exception exception)
            {
                throw new CredentialInteropException(CredentialDiagnosticStage.NativeSymbols,
                    CredentialFailureCode.NativeSymbolFailure, exception);
            }
        }
    }

    private sealed class OwnedCf(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() { if (Handle != IntPtr.Zero) CFRelease(Handle); }
        public static OwnedCf String(string value)
        {
            var handle = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100);
            return handle != IntPtr.Zero ? new(handle) : throw new CredentialInteropException(
                CredentialDiagnosticStage.CoreFoundationString,
                CredentialFailureCode.CoreFoundationStringFailure,
                new InvalidOperationException());
        }
        public static OwnedCf Data(byte[] value)
        {
            var handle = CFDataCreate(IntPtr.Zero, value, value.Length);
            return handle != IntPtr.Zero ? new(handle) : throw new CredentialInteropException(
                CredentialDiagnosticStage.CoreFoundationData,
                CredentialFailureCode.CoreFoundationDataFailure,
                new InvalidOperationException());
        }
        public static OwnedCf Dictionary(IReadOnlyList<(IntPtr Key, IntPtr Value)> entries)
        {
            var keys = entries.Select(item => item.Key).ToArray();
            var values = entries.Select(item => item.Value).ToArray();
            var handle = CFDictionaryCreate(IntPtr.Zero, keys, values, entries.Count,
                Symbols.Value.DictionaryKeyCallbacks,
                Symbols.Value.DictionaryValueCallbacks);
            return handle != IntPtr.Zero ? new(handle) : throw new CredentialInteropException(
                CredentialDiagnosticStage.CoreFoundationDictionary,
                CredentialFailureCode.CoreFoundationDictionaryFailure,
                new InvalidOperationException());
        }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecItemAdd(IntPtr attributes, out IntPtr result);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecItemDelete(IntPtr query);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDataGetLength(IntPtr data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys,
        IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDictionaryGetCount(IntPtr dictionary);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFGetTypeID(IntPtr value);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFStringGetTypeID();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFDataGetTypeID();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFDictionaryGetTypeID();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFBooleanGetTypeID();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);
}
