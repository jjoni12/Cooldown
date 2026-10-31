using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LimitBar.Providers
{
    public sealed class CommandProvider : IUsageProvider
    {
        public static readonly string ConfigFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LimitBar", "services.json");

        readonly string _command;
        readonly string _arguments;
        readonly TimeSpan _timeout;

        public string Id { get; }
        public string Name { get; }
        public bool IsSetUp => true;

        public CommandProvider(string id, string name, string command, string arguments, TimeSpan timeout)
        {
            Id = id;
            Name = name;
            _command = command;
            _arguments = arguments ?? "";
            _timeout = timeout;
        }

        public static IEnumerable<IUsageProvider> Load(ICollection<string> takenIds)
        {
            if (!File.Exists(ConfigFile)) yield break;
            object root;
            try
            {
                root = Json.Parse(File.ReadAllText(ConfigFile));
            }
            catch (Exception ex)
            {
                Log.Error("services.json", ex.Message);
                yield break;
            }
            foreach (var entry in Json.Arr(root))
            {
                var id = Json.Str(Json.Get(entry, "id"));
                var command = Json.Str(Json.Get(entry, "command"));
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(command) || takenIds.Contains(id))
                {
                    Log.Error("services.json", $"skipped an entry (id '{id}'): needs a unique id and a command");
                    continue;
                }
                takenIds.Add(id);
                var seconds = Json.Num(Json.Get(entry, "timeoutSeconds")) ?? 30;
                yield return new CommandProvider(id, Json.Str(Json.Get(entry, "name")) ?? id, command,
                    Json.Str(Json.Get(entry, "arguments")), TimeSpan.FromSeconds(Math.Max(1, seconds)));
            }
        }

        public async Task<ServiceUsage> FetchAsync(CancellationToken ct)
        {
            var result = await Cli.RunAsync(Environment.ExpandEnvironmentVariables(_command), Environment.ExpandEnvironmentVariables(_arguments), _timeout, ct: ct);
            if (result.TimedOut) throw new UsageException(Text.CommandTimedOut(Name));
            var json = Json.ExtractObject(result.Stdout);
            if (json == null) throw new UsageException(Text.CommandBadOutput(Name));
            var root = Json.Parse(json);

            var error = Json.Str(Json.Get(root, "error"));
            if (!string.IsNullOrEmpty(error)) throw new UsageException(error);

            var usage = new ServiceUsage { Id = Id, Name = Name };
            foreach (var window in Json.Arr(Json.Get(root, "windows")))
            {
                var seconds = Json.Num(Json.Get(window, "lengthSeconds"));
                var used = Json.Num(Json.Get(window, "usedPercent"));
                if (!seconds.HasValue || seconds.Value <= 0 || !used.HasValue) continue;
                var length = TimeSpan.FromSeconds(seconds.Value);
                usage.Windows.Add(new UsageWindow
                {
                    Label = Text.WindowLabel(length),
                    Length = length,
                    UsedPercent = Math.Max(0, Math.Min(100, used.Value)),
                    ResetsAt = Json.Time(Json.Get(window, "resetsAt")),
                });
            }
            usage.Windows = usage.Windows.OrderBy(w => w.Length).ToList();
            return usage;
        }
    }
}
