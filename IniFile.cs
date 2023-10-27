﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.CodeDom;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;

namespace PresetConverter
{
	// Comes from Patrick Mours' ReShade Installer
	public class IniFile
	{
		readonly string filePath;

		SortedDictionary<string, SortedDictionary<string, string[]>> sections =
			new SortedDictionary<string, SortedDictionary<string, string[]>>();

		public IniFile(string path) : this(File.Exists(path) ? new FileStream(path, FileMode.Open) : null)
		{
			filePath = path;
		}
		public IniFile(Stream stream)
		{
			if (stream == null)
			{
				return;
			}
			
			var section = string.Empty;

			using (var reader = new StreamReader(stream, Encoding.UTF8))
			{
				while (!reader.EndOfStream)
				{
					string line = reader.ReadLine().Trim();
					if (string.IsNullOrEmpty(line) ||
						line.StartsWith(";", StringComparison.Ordinal) ||
						line.StartsWith("#", StringComparison.Ordinal) ||
						line.StartsWith("//", StringComparison.Ordinal)
					)
					{
						continue;
					}

					if (line.StartsWith("[", StringComparison.Ordinal))
					{
						int sectionEnd = line.IndexOf(']');
						if (sectionEnd >= 0)
						{
							section = line.Substring(1, sectionEnd - 1);
							continue;
						}
					}
					var pair = line.Split(new[] { '=' }, 2, StringSplitOptions.None);
					if (pair.Length == 2 && pair[0].Trim() is var key && pair[1].Trim() is var value)
					{
						SetValue(section, key, value.Split(new[] { ',' }, StringSplitOptions.None));
					}
					else
					{
						SetValue(section, line);
					}
				}
			}
		}

		public void SaveFile()
		{
			if (filePath == null)
			{
				throw new InvalidOperationException();
			}
			SaveFile(filePath);
		}
		public void SaveFile(string path)
		{
			var text = new StringBuilder();

			foreach (var section in sections)
			{
				if (!string.IsNullOrEmpty(section.Key))
				{
					text.AppendLine("[" + section.Key + "]");
				}
				foreach (var pair in section.Value)
				{
					text.AppendLine(pair.Key + "=" + string.Join(",", pair.Value));
				}
				text.AppendLine();
			}
			text.AppendLine();
			File.WriteAllText(path, text.ToString(), Encoding.UTF8);
		}

		public bool HasValue(string section)
		{
			return sections.ContainsKey(section);
		}
		public bool HasValue(string section, string key)
		{
			return sections.TryGetValue(section, out var sectionData) && sectionData.ContainsKey(key);
		}
		public bool GetValue(string section, string key, out string[] value)
		{
			if (!sections.TryGetValue(section, out var sectionData))
			{
				value = default; return false;
			}
			return sectionData.TryGetValue(key, out value);
		}
		public void SetValue(string section, string key, params string[] value)
		{
			if (!sections.TryGetValue(section, out var sectionData))
			{
				if (value == null)
				{
					return;
				}
				sectionData = new SortedDictionary<string, string[]>();
				sections[section] = sectionData;
			}
			sectionData[key] = value ?? new string[] { };
		}
		public void RenameValue(string section, string key, string newKey)
		{
			var value = GetString(section, key);
			if (value != null)
			{
				SetValue(section, newKey, value);
				RemoveValue(section, key);
			}
		}
		public void RenameValue(string section, string key, string newSection, string newKey)
		{
			var value = GetString(section, key);
			if (value != null)
			{
				SetValue(newSection, newKey, value);
				RemoveValue(section, key);
			}
		}
		public void RemoveValue(string section, string key)
		{
			if (sections.TryGetValue(section, out var sectionData))
			{
				sectionData.Remove(key);
				if (sectionData.Count == 0)
				{
					sections.Remove(section);
				}
			}
		}
		public string GetString(string section, string key, string defaultValue = null)
		{
			return GetValue(section, key, out var value) ? string.Join(",", value) : defaultValue;
		}

		public string[] GetSections()
		{
			return sections.Select(x => x.Key).ToArray();
		}

		public void SetSection(string section, SortedDictionary<string, string[]> sectionData)
		{
			if (!sections.ContainsKey(section))
            {
                sections.Add(section, sectionData);
            }
            else
			{
				sections[section] = sectionData;
			}
		}
        public void GetSection(string section, out SortedDictionary<string, string[]> sectionData)
        {
            if (HasValue(section))
            {
                sectionData = sections[section];
            }
            else
            {
                sectionData = null;
            }
        }
        public void RemoveSection(string section)
		{
			if (sections.ContainsKey(section))
			{
				sections.Remove(section);
			}
		}
		public void RenameSection(string section, string newSection)
		{
			if (sections.TryGetValue(section, out var sectionData))
			{
				SetSection(newSection, sectionData);
				RemoveSection(section);
            }
		}
	}

	public class GShadePreset
	{
        string[] flairs;
		string[] techniques;
		readonly IniFile gshade_preset;
		IniFile[] reshade_presets;

		public string srcVersion { get; set; } = "gs@403";
		public string dstVersion { get; set; } = "re@580";
		public bool shouldCleanSorting { get; set; } = false;
		public bool shouldMovePreprocessors { get; set; } = false;

        GShadePreset(IniFile gs_preset)
		{
			gshade_preset = gs_preset;
			gshade_preset.GetValue("", "Flairs", out flairs);
			gshade_preset.GetValue("", "Techniques", out techniques);
		}

        /// <summary>
        /// 根据GShade预设及变体名解析相应变体的独立版预设。
        /// </summary>
        /// <param name="inFlair">变体名，空字符串对应解析本体。</param>
        /// <returns></returns>
        public IniFile ExtractFlair(string inFlair)
		{
			IniFile preset = null;
			gshade_preset.GetSection("", out var headSectionData);
			preset.SetSection("", headSectionData);
			foreach (var technique in techniques)
			{
				var techFlair = inFlair.Length > 0 ? (GetTechniqueFileName(technique) + "|" + inFlair) : GetTechniqueFileName(technique);
				SortedDictionary<string, string[]> sectionData;
				if (gshade_preset.HasValue(techFlair)) // 找到变体
				{
					gshade_preset.GetSection(techFlair, out sectionData);
					preset.SetSection(technique, sectionData);
				}
				else if (gshade_preset.HasValue(technique)) // 没找到同名变体但有本体，使用本体
				{
					gshade_preset.GetSection(technique, out sectionData);
					preset.SetSection(technique, sectionData);
				}
				else
				{
					var flag = false;
					foreach (var flair in flairs) // 没找到同名变体也没找到本体，随便抓一个
                    {
						techFlair = technique + "|" + flair;
						if (gshade_preset.HasValue(techFlair))
						{
                            gshade_preset.GetSection(techFlair, out sectionData);
                            preset.SetSection(technique, sectionData);
                            flag = true; // 找到了
							break;
						}
					}
					if (!flag) // 还是没找到，使用空白表
					{
						//preset.SetSection(technique, null);
					}
				}
			}
			return preset;
		}

		public void CleanSorting(IniFile preset)
		{
			preset.RemoveValue("", "TechniqueSorting");
		}

		public void MovePreprocessors(IniFile preset)
		{

		}

		string GetTechniqueFileName(string techniqueSignature)
		{
			var index = techniqueSignature.IndexOf('@');
			if (index > 0 && index != techniqueSignature.Length - 1)
			{
				return techniqueSignature.Substring(index + 1);
			}
			return techniqueSignature;
		}

	}

	public class ReversePreprocessor
	{

	}
}
