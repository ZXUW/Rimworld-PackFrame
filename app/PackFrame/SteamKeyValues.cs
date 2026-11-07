using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace PackFrame;

// A bounded reader for Steam's text KeyValues files. It never opens or changes files.
internal sealed class SteamKeyValues
{
    private const int MaximumInputBytes = 8 * 1024 * 1024;
    private const int MaximumDepth = 32;
    private const int MaximumEntries = 50000;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SteamKeyValues> _children = new(StringComparer.OrdinalIgnoreCase);

    private SteamKeyValues() => Children = new ReadOnlyDictionary<string, SteamKeyValues>(_children);

    public IReadOnlyDictionary<string, SteamKeyValues> Children { get; }

    public string? Value(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public SteamKeyValues? Child(string key) => _children.TryGetValue(key, out var child) ? child : null;

    public static bool TryParse(string text, out SteamKeyValues root)
    {
        root = new SteamKeyValues();
        if (text is null || text.Length > MaximumInputBytes ||
            Encoding.UTF8.GetByteCount(text) > MaximumInputBytes)
            return false;

        var parsed = new SteamKeyValues();
        var parser = new Parser(text);
        if (!parser.ReadObject(parsed, 0, isRoot: true) ||
            parsed._values.Count + parsed._children.Count == 0)
            return false;
        root = parsed;
        return true;
    }

    private bool Add(string key, string value)
    {
        if (_children.ContainsKey(key)) return false;
        if (_values.TryGetValue(key, out var existing))
            return string.Equals(existing, value, StringComparison.Ordinal);
        _values.Add(key, value);
        return true;
    }

    private bool Add(string key, SteamKeyValues child)
    {
        if (_values.ContainsKey(key)) return false;
        if (_children.TryGetValue(key, out var existing)) return existing.Matches(child);
        _children.Add(key, child);
        return true;
    }

    private bool Matches(SteamKeyValues other)
    {
        if (_values.Count != other._values.Count || _children.Count != other._children.Count)
            return false;
        foreach (var pair in _values)
            if (!other._values.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
                return false;
        foreach (var pair in _children)
            if (!other._children.TryGetValue(pair.Key, out var child) || !pair.Value.Matches(child))
                return false;
        return true;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;
        private int _entries;

        public Parser(string text)
        {
            _text = text;
            if (text.Length > 0 && text[0] == '\uFEFF') _position = 1;
        }

        public bool ReadObject(SteamKeyValues target, int depth, bool isRoot = false)
        {
            while (true)
            {
                SkipTrivia();
                if (_position == _text.Length) return isRoot;
                if (_text[_position] == '}')
                {
                    if (isRoot) return false;
                    _position++;
                    return true;
                }
                if (!ReadToken(out var key)) return false;
                SkipTrivia();
                if (_position == _text.Length || ++_entries > MaximumEntries) return false;
                if (_text[_position] == '{')
                {
                    if (depth >= MaximumDepth) return false;
                    _position++;
                    var child = new SteamKeyValues();
                    if (!ReadObject(child, depth + 1) || !target.Add(key, child)) return false;
                }
                else if (!ReadToken(out var value) || !target.Add(key, value)) return false;
            }
        }

        private void SkipTrivia()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                    continue;
                }
                if (_text[_position] != '/' || _position + 1 >= _text.Length ||
                    _text[_position + 1] != '/') return;
                _position += 2;
                while (_position < _text.Length && _text[_position] != '\r' && _text[_position] != '\n')
                    _position++;
            }
        }

        private bool ReadToken(out string token)
        {
            token = string.Empty;
            if (_position >= _text.Length || _text[_position] is '{' or '}') return false;
            if (_text[_position] != '"')
            {
                var start = _position;
                while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) &&
                       _text[_position] is not ('{' or '}' or '"'))
                {
                    if (char.IsControl(_text[_position])) return false;
                    _position++;
                }
                if (_position == start) return false;
                token = _text[start.._position];
                return true;
            }

            _position++;
            var result = new StringBuilder();
            while (_position < _text.Length)
            {
                var current = _text[_position++];
                if (current == '"')
                {
                    token = result.ToString();
                    return true;
                }
                if (current == '\0') return false;
                if (current != '\\')
                {
                    result.Append(current);
                    continue;
                }
                if (_position == _text.Length) return false;
                var escaped = _text[_position++];
                if (escaped == '\0') return false;
                // Decode only the two unambiguous Steam string escapes. Keep unknown
                // sequences literally so unescaped Windows paths never lose slashes.
                if (escaped is not ('\\' or '"')) result.Append('\\');
                result.Append(escaped);
            }
            return false;
        }
    }
}
