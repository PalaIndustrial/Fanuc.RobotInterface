using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace FANUCRobotTest
{
    public class SettingsBase : ObservableObject
    {
        private static JsonSerializerSettings _SerializerSettings = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            DefaultValueHandling = DefaultValueHandling.Populate,
            TypeNameHandling = TypeNameHandling.All,
        };

        private readonly string _FilePath;

        public SettingsBase(string filename)
        {
            _FilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FANUCRobotTest",
                filename
            );
            Load();
        }

        public void Load()
        {
            string json;
            try
            {
                json = File.ReadAllText(_FilePath);
            }
            catch
            {
                json = "{}";
            }
            JsonConvert.PopulateObject(json, this, _SerializerSettings);
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_FilePath));

            if (File.Exists($"{_FilePath}.new"))
                File.Delete($"{_FilePath}.new");

            File.WriteAllText($"{_FilePath}.new", JsonConvert.SerializeObject(this, _SerializerSettings));

            if (File.Exists($"{_FilePath}.bak"))
                File.Delete($"{_FilePath}.bak");
            if (File.Exists(_FilePath))
                File.Move(_FilePath, $"{_FilePath}.bak");
            File.Move($"{_FilePath}.new", _FilePath);
        }
    }
}
