using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace VNdev.Core.Tests;

/// <summary>Đánh dấu một phương thức là test để bộ chạy tự tìm thấy.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
    public string? Name { get; }
    public TestAttribute(string? name = null) => Name = name;
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}

/// <summary>
/// Bộ kiểm chứng tối giản, không phụ thuộc thư viện ngoài.
/// </summary>
/// <remarks>
/// Cố tình không dùng xUnit hay NUnit: bộ test phải chạy được bằng đúng một
/// lệnh <c>dotnet run</c> trên máy sạch, không cần tải gói NuGet nào. Với một
/// dự án mà người dùng sẽ tự build, bớt được một bước phụ thuộc mạng là đáng.
/// </remarks>
public static class Check
{
    public static void True(bool condition, string? because = null)
    {
        if (!condition) throw new AssertionException(because ?? "Mong đợi đúng nhưng nhận được sai.");
    }

    public static void False(bool condition, string? because = null)
    {
        if (condition) throw new AssertionException(because ?? "Mong đợi sai nhưng nhận được đúng.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException($"Mong đợi <{Describe(expected)}> nhưng nhận được <{Describe(actual)}>.");
        }
    }

    public static void NotEqual<T>(T unexpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
        {
            throw new AssertionException($"Không mong đợi giá trị <{Describe(actual)}> nhưng vẫn nhận được nó.");
        }
    }

    public static void Null(object? value)
    {
        if (value is not null) throw new AssertionException($"Mong đợi null nhưng nhận được <{Describe(value)}>.");
    }

    public static void NotNull([NotNull] object? value)
    {
        if (value is null) throw new AssertionException("Mong đợi khác null nhưng nhận được null.");
    }

    public static void Empty<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.Count != 0)
        {
            throw new AssertionException($"Mong đợi rỗng nhưng có {list.Count} phần tử: {Describe(list.First())}");
        }
    }

    public static T Single<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.Count != 1) throw new AssertionException($"Mong đợi đúng 1 phần tử nhưng có {list.Count}.");
        return list[0];
    }

    public static T Single<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var matched = items.Where(predicate).ToList();
        if (matched.Count != 1)
        {
            throw new AssertionException($"Mong đợi đúng 1 phần tử khớp điều kiện nhưng có {matched.Count}.");
        }
        return matched[0];
    }

    public static void Contains<T>(IEnumerable<T> items, Func<T, bool> predicate, string? because = null)
    {
        if (!items.Any(predicate))
        {
            throw new AssertionException(because ?? "Không tìm thấy phần tử nào khớp điều kiện.");
        }
    }

    public static void DoesNotContain<T>(IEnumerable<T> items, Func<T, bool> predicate, string? because = null)
    {
        if (items.Any(predicate))
        {
            throw new AssertionException(because ?? "Tìm thấy phần tử khớp điều kiện, lẽ ra không được có.");
        }
    }

    /// <summary>Tập hợp có chứa đúng giá trị này không.</summary>
    public static void Contains<T>(T value, IEnumerable<T> items)
    {
        if (!items.Contains(value))
        {
            throw new AssertionException($"Không tìm thấy <{Describe(value)}> trong tập hợp.");
        }
    }

    public static void DoesNotContain<T>(T value, IEnumerable<T> items)
    {
        if (items.Contains(value))
        {
            throw new AssertionException($"Tìm thấy <{Describe(value)}> trong tập hợp, lẽ ra không được có.");
        }
    }

    /// <summary>Hai dãy có cùng phần tử theo đúng thứ tự không.</summary>
    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var left = expected.ToList();
        var right = actual.ToList();
        if (!left.SequenceEqual(right))
        {
            throw new AssertionException(
                $"Dãy khác nhau.\n  Mong đợi: [{string.Join(", ", left)}]\n  Nhận được: [{string.Join(", ", right)}]");
        }
    }

    /// <summary>Chuỗi con mong đợi đứng trước, chuỗi thật đứng sau.</summary>
    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new AssertionException($"Chuỗi không chứa \"{expected}\".\nNội dung: {Truncate(actual)}");
        }
    }

    public static void DoesNotContain(string unexpected, string actual)
    {
        if (actual.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new AssertionException($"Chuỗi chứa \"{unexpected}\" nhưng lẽ ra không được.\nNội dung: {Truncate(actual)}");
        }
    }

    public static void StartsWith(string prefix, string actual)
    {
        if (!actual.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new AssertionException($"Mong đợi bắt đầu bằng \"{prefix}\" nhưng nhận được \"{actual}\".");
        }
    }

    public static T IsType<T>(object? value)
    {
        if (value is not T typed)
        {
            throw new AssertionException($"Mong đợi kiểu {typeof(T).Name} nhưng nhận được {value?.GetType().Name ?? "null"}.");
        }
        return typed;
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new AssertionException($"Mong đợi ngoại lệ {typeof(T).Name} nhưng nhận được {other.GetType().Name}: {other.Message}");
        }
        throw new AssertionException($"Mong đợi ngoại lệ {typeof(T).Name} nhưng không có ngoại lệ nào.");
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static string Truncate(string text, int max = 200)
        => text.Length <= max ? text : text[..max] + "…";
}

public static class TestRunner
{
    /// <summary>Tìm mọi phương thức gắn [Test] trong assembly rồi chạy tuần tự.</summary>
    public static int Run()
    {
        var methods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var passed = 0;
        var failures = new List<string>();
        string? currentGroup = null;

        foreach (var method in methods)
        {
            var group = method.DeclaringType!.Name;
            if (group != currentGroup)
            {
                Console.WriteLine();
                Console.WriteLine($"  {group}");
                currentGroup = group;
            }

            var label = method.GetCustomAttribute<TestAttribute>()!.Name ?? Humanize(method.Name);

            try
            {
                var instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, null);
                Console.WriteLine($"    ✓ {label}");
                passed++;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                Console.WriteLine($"    ✗ {label}");
                Console.WriteLine($"        {inner.Message.Replace("\n", "\n        ")}");
                failures.Add($"{group}.{method.Name}");
            }
        }

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine($"Tất cả {passed} test đều pass.");
            return 0;
        }

        Console.WriteLine($"{passed} pass, {failures.Count} hỏng:");
        foreach (var failure in failures) Console.WriteLine($"  - {failure}");
        return 1;
    }

    private static string Humanize(string methodName) => methodName.Replace('_', ' ');
}
