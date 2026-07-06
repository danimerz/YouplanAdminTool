using System.Text;

namespace YouplanAdminTool.Infrastructure.Http;

internal sealed class QueryStringBuilder
{
    private readonly List<string> _parts = [];

    public QueryStringBuilder Add(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        return this;
    }

    public QueryStringBuilder Add(string key, int value) => Add(key, value.ToString());

    public QueryStringBuilder AddEach(string key, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return this;
        }

        foreach (var value in values)
        {
            Add(key, value);
        }

        return this;
    }

    public string Build(string path)
    {
        if (_parts.Count == 0)
        {
            return path;
        }

        var sb = new StringBuilder(path).Append('?');
        sb.Append(string.Join('&', _parts));
        return sb.ToString();
    }
}
