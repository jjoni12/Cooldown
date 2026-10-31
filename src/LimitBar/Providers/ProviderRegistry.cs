using System.Collections.Generic;
using System.Linq;

namespace LimitBar.Providers
{
    static class ProviderRegistry
    {
        public static IUsageProvider[] Create()
        {
            if (DemoProvider.On) return DemoProvider.All();
            var builtIn = new List<IUsageProvider>
            {
                new ClaudeProvider(),
                new CodexProvider(),
                new CursorProvider(),
                new CopilotProvider(),
                new AntigravityProvider(),
                new GrokProvider(),
                new OpenCodeProvider(),
            };
            var ids = new HashSet<string>(builtIn.Select(p => p.Id));
            return builtIn.Concat(CommandProvider.Load(ids)).ToArray();
        }
    }
}
