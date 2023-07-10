namespace ExampleNet
{
    public static class Extensions
    {
        public static List<string> AllConfigurationKeys(this IConfigurationRoot root)
        {
            (string Value, IConfigurationProvider Provider) GetValueAndProvider(IConfigurationRoot root, string key)
            {
                foreach (IConfigurationProvider provider in root.Providers.Reverse())
                {
                    if (provider.TryGet(key, out string value))
                    {
                        return (value, provider);
                    }
                }

                return (null, null);
            }

            void RecurseChildren(HashSet<string> keys, IEnumerable<IConfigurationSection> children, string rootPath)
            {
                foreach (IConfigurationSection child in children)
                {
                    (string Value, IConfigurationProvider Provider) valueAndProvider = GetValueAndProvider(root, child.Path);

                    if (valueAndProvider.Provider != null)
                    {
                        keys.Add(rootPath + ":" + child.Key + "=" + valueAndProvider.Value);
                    }

                    RecurseChildren(keys, child.GetChildren(), child.Path);
                }
            }

            var keys = new HashSet<string>();
            RecurseChildren(keys, root.GetChildren(), "");
            return keys.ToList();
        }

        public static void LogStrings(this List<string> list)
        {
            foreach (var e in list)
            {
                Serilog.Log.Information(e);
            }
        }
    }
}
