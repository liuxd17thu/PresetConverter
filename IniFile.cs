using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.CodeDom;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Collections.ObjectModel;
using System.Collections;
using System.Runtime.InteropServices;
using System.Net.Http.Headers;
using System.Xml.XPath;
using System.Net;

namespace PresetConverter
{
	// Comes from Patrick Mours' ReShade Installer
	public class IniFile
	{
		public string filePath;

		SortedDictionary<string, SortedDictionary<string, string[]>> sections =
			new SortedDictionary<string, SortedDictionary<string, string[]>>();

		public IniFile(string path, bool isEmpty = false) : this(File.Exists(path) && !isEmpty ? new FileStream(path, FileMode.Open) : null)
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

		public string GetIniFileName()
		{
			return Path.GetFileName(filePath);
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
        public string[] flairs;
		public string[] techniques;
		public SortedSet<string> techniqueFiles;
		public IniFile gshade_preset;
		public SortedDictionary<string, IniFile> reshade_presets = new SortedDictionary<string, IniFile>();

		public bool HasUnusedPreprocessor = false;

        public GShadePreset(IniFile gs_preset)
		{
			gshade_preset = gs_preset;
			gshade_preset.GetValue("", "Flairs", out flairs);
			gshade_preset.GetValue("", "Techniques", out techniques);
			if (flairs == null)
				UpgradeClones();
			else
				CleanClones();
            techniqueFiles = new SortedSet<string>(techniques.Select(x => GetTechniqueFileName(x)).ToHashSet());
		}

		private void UpgradeClones()
		{
			var new_flairs = new SortedSet<string>();
			var new_techniques = new List<string>();
			foreach (var technique in techniques)
			{
				var new_technique = technique;
				if (technique.Contains("+"))
				{
					var x = technique.Split(new[] { '+' }, 2, StringSplitOptions.None);
					if (x.Length == 2)
						new_flairs.Add(x[1]);
					new_technique = x[0];
				}
				if (!new_techniques.Contains(new_technique))
					new_techniques.Add(new_technique);
			}

			flairs = new_flairs.ToArray();
			techniques = new_techniques.ToArray();

			if (flairs.Length > 0)
			{
				Console.WriteLine($"WARN|该预设包含GShade 3.x的着色器克隆语法，因此升级到后续的预设模板形式以供拆分");
                gshade_preset.SetValue("", "Techniques", techniques);
                gshade_preset.SetValue("", "Flairs", flairs.ToArray());
            }

			var effects = gshade_preset.GetSections().Where(x => x != "").ToArray();
			foreach (var effect in effects)
			{
				var new_effect = effect.Replace('+', '|');
				if (new_effect != effect)
				{
					gshade_preset.RenameSection(effect, new_effect);
				}
			}
		}

		private void CleanClones()
		{
			var new_techniques = techniques.Where(x => !x.Contains('+')).ToArray();

			if (new_techniques.Length < techniques.Length)
				Console.WriteLine($"WARN|该预设混杂了GShade 3.x的着色器克隆与4.x的预设模板，因此去除着色器克隆");

			gshade_preset.SetValue("", "Techniques", new_techniques);

			foreach (var effect in gshade_preset.GetSections().Where(x => x.Contains('+')))
			{
				gshade_preset.RemoveSection(effect);
			}
		}

        /// <summary>
        /// 根据GShade预设及变体名解析相应变体的独立版预设。
		/// 需要在移动预处理器定义之前使用。
        /// </summary>
        /// <param name="inFlair">变体名，空字符串对应解析本体。</param>
        /// <returns>拆分后的预设文件。</returns>
        public void ExtractFlair(string inFlair)
		{
			var preset = new IniFile(Path.Combine(Path.GetFileNameWithoutExtension(gshade_preset.filePath) + (inFlair == "" ? "" : " - ") + inFlair + ".ini"), true);
            Console.WriteLine($"INFO|提取预设模板：{preset.GetIniFileName()}");

            gshade_preset.GetSection("", out var headSectionData);
			preset.SetSection("", headSectionData);
			foreach (var technique in techniqueFiles)
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
                // 高版本GS -> 高版本RE，迁移预处理器
                if (gshade_preset.GetValue(technique, "PreprocessorDefinitions", out var pp))
                {
                    preset.SetValue(technique, "PreprocessorDefinitions", pp);
                }
            }
			preset.RemoveValue("", "Flairs");
			preset.SetValue("", "FeatureLevel", new string[] { "50800" });
			reshade_presets.Add(inFlair, preset);
		}

		/// <summary>
		/// 清除TechniqueSorting区段。
		/// </summary>
		/// <param name="preset">输入预设。</param>
		public void CleanSorting(IniFile preset)
		{
			preset.RemoveValue("", "TechniqueSorting");
		}

        /// <summary>
        /// 移动预处理器定义至相应区段。
        /// 只能用于预设模板拆分后的预设。
        /// </summary>
        /// <param name="preset">输入预设文件，原位操作。</param>
        /// <param name="ppList">预处理器表以及对应的反查表。</param>
        public void MovePreprocessors(IniFile preset, in PreprocessorList ppList)
		{
			// 具有自己section的着色器列表（可能多于Technique键值）
			var effects = preset.GetSections().Where(x => x != "").ToArray();

            // 获取所有的预处理器定义。
            Func<string[], Dictionary<string, string>> buildPair = delegate (string[] list)
			{
				return list.Select(x =>
				{   
					return x.Split(new char[] { '=' }, 2, StringSplitOptions.None);
				}).Where(
					x => x.Length == 2
				).ToDictionary(x => x[0], x => x[1]);
            };
			// preset section的预处理器定义
			if (!preset.GetValue("", "PreprocessorDefinitions", out var tmp))
				return;
            var preset_pp = new SortedDictionary<string, string>(buildPair(tmp));

            foreach (var effect in effects)
			{
				if (!ppList.forwardList.ContainsKey(effect))
					continue;

				// effect section的预处理器定义
				preset.GetValue(effect, "PreprocessorDefinitions", out tmp);
				var section_pp = tmp != null ? new SortedDictionary<string, string>(buildPair(tmp)) : new SortedDictionary<string, string>() { };

                foreach (var pp in ppList.forwardList[effect])
				{
					if (preset_pp.ContainsKey(pp) && !section_pp.ContainsKey(pp))
					{
						section_pp[pp] = preset_pp[pp];
						preset_pp.Remove(pp);
					}
				}
				preset.SetValue(effect, "PreprocessorDefinitions", section_pp.Select(x => x.Key + "=" + x.Value).ToArray());
			}
			if (preset_pp.Count > 0)
			{
				Console.WriteLine($"WARN|{preset.GetIniFileName()}似乎未使用的预处理器定义：");

				HasUnusedPreprocessor = true;
                foreach (var pp in preset_pp)
				{
                    Console.WriteLine($"    |  {pp.Key + "=" + pp.Value}");
                }

				if(preset_pp.Count > 0)
					preset.SetValue("", "PreprocessorDefinitions", preset_pp.Select(x => x.Key + "=" + x.Value).ToArray());
				return;
			}
			//preset.RemoveValue("", "PreprocessorDefinitions");
		}

        /// <summary>
        /// 清理没有实际使用的效果器Section。
		/// 只能用于预设模板拆分后的预设。
        /// </summary>
        /// <param name="preset">输入预设文件，原位操作。</param>
        public void RemoveUnusedTechniques(IniFile preset)
		{
			//var techniques = new SortedSet<string>(this.techniques.Select(x => GetTechniqueFileName(x)).ToHashSet());
			var effects = preset.GetSections().Where(x => x != "").ToArray();

			foreach (var effect in effects)
			{
				var effectFile = effect.Split(new char[] { '|' }, 2, StringSplitOptions.None);
				if (!techniqueFiles.Contains(effectFile[0]))
				{
					preset.RemoveSection(effect);
				}
			}
		}

		/// <summary>
		/// 修复MultiLUT.fx问题。
		/// 只能用于预设模板拆分，且移动了预处理器定义的预设。
		/// </summary>
		/// <param name="preset">输入预设文件，原位操作。</param>
		/// <param name="level">MultiLUT修复等级。</param>
		public void FixMultiLUT(IniFile preset, in int level)
		{
			var featureLevel = 40900;
			var mlut_pp = new SortedDictionary<string, string>();
            // 获取所有的预处理器定义
            Func<string[], Dictionary<string, string>> buildPair = delegate (string[] list)
            {
                return list.Select(x =>
                {
                    return x.Split(new char[] { '=' }, 2, StringSplitOptions.None);
                }).Where(
                    x => x.Length == 2
                ).ToDictionary(x => x[0], x => x[1]);
            };

            if (preset.GetValue("", "FeatureLevel", out var tmp))
			{
				featureLevel = tmp[0] != "" ? int.Parse(tmp[0]) : 40900;
				featureLevel = Math.Max(40900, featureLevel);
			}
			if (!preset.GetValue("MultiLUT.fx", "PreprocessorDefinitions", out tmp))
				return;
            mlut_pp = new SortedDictionary<string, string>(buildPair(tmp));

			var iterate = new string[] { "", "2", "3" };
			
			// 单自定义升级到三自定义
			foreach (var i in iterate)
			{
				var multiLUTTexture = "MultiLUTTexture" + i;
				var multiLUTTexture_Source = "MultiLUTTexture" + i + "_Source";
				// MultiLUT的通道i是否启用？
                if (!mlut_pp.ContainsKey(multiLUTTexture) || mlut_pp[multiLUTTexture] == "0")
					continue;
				// 启用的通道i是否要用自定义色条素材？
				if (!mlut_pp.ContainsKey(multiLUTTexture_Source) || mlut_pp[multiLUTTexture_Source] != "1")
					continue;
				// 如果某个Pass的自定义LUT没写文件名
				if (!mlut_pp.ContainsKey("fLUT_TextureName" + i))
				{
					var flag = false;
					// 两种情况：Pass1用自定义LUT但是没写文件名，相当于用默认
					// 或者旧版Pass2、3用公共自定义LUT，但是没写公共LUT文件名，也相当于默认
					if (i == "" || !mlut_pp.ContainsKey("fLUT_TextureName"))
					{
						if (flag == false)
							Console.WriteLine($"WARN|{preset.GetIniFileName()}使用了自定义LUT但并没有指定文件名，相当于使用ReShade MultiLUT，强行修复");
						flag = true;
						mlut_pp[multiLUTTexture_Source] = "2";
					}
					// 旧版Pass2、3用公共自定义LUT，转移文件名
					else
					{
						mlut_pp.Add("fLUT_TextureName" + i, mlut_pp["fLUT_TextureName"]);
					}
				}
				// Pass2、3写了文件名说明已经是三自定义版本了，无需额外操作
			}

			if (level == 0 || level == 1 && featureLevel > 50700)
			{
				if (level == 1)
					//Console.WriteLine($"INFO|{preset.GetIniFileName()}似乎是一个新版GS预设，跳过17/18行问题的修复");
				preset.SetValue("MultiLUT.fx", "PreprocessorDefinitions", mlut_pp.Select(x => x.Key + "=" + x.Value).ToArray());
				return;
			}
			//Console.WriteLine($"INFO|{preset.GetIniFileName()}似乎需要进行17/18行问题修复\n    |请注意该修复为推测性质，存在将正常预设修坏的可能");
			// 17 -> 18 问题
			foreach (var i in iterate)
			{
                var multiLUTTexture = "MultiLUTTexture" + i;
                var multiLUTTexture_Source = "MultiLUTTexture" + i + "_Source";
                // MultiLUT的通道i是否启用？
                if (!mlut_pp.ContainsKey(multiLUTTexture) || mlut_pp[multiLUTTexture] == "0")
                    continue;
				// MultiLUT_atlas4的17->18升级，以匹配更换后的图片
				if (mlut_pp.TryGetValue(multiLUTTexture_Source, out var source) && source == "2")
				{
					preset.GetValue("MultiLUT.fx", "fLUT_LutSelector" + i, out var selector);
					var int_selector = int.Parse(selector[0]);
					if (int_selector >= 14 && int_selector <= 16)
					{
						Console.WriteLine($"INFO|{preset.GetIniFileName()}的Pass{(i == "" ? "1" : i)}使用了ReShade MultiLUT\n    |因此升级到18行版本");
						preset.SetValue("MultiLUT.fx", "fLUT_LutSelector" + i, new[] { (int_selector + 1).ToString() });
					}
				}
				// 自定义MultiLUT在没有说明行数的情况下，回退到以17读取，以和图片素材读取匹配
				else if (mlut_pp.TryGetValue(multiLUTTexture_Source, out source) && source == "1")
				{
					// 自定义了MultiLUT文件，但没填MultiLUT行数
					if (mlut_pp.TryGetValue(multiLUTTexture_Source, out var mlut_tex) && mlut_tex.Length > 0 && !mlut_pp.ContainsKey("fLUT_LutAmount" + i))
					{
                        Console.WriteLine($"INFO|{preset.GetIniFileName()}的Pass{(i == "" ? "1" : i)}使用了自定义MultiLUT但忘记给出行数\n    |因此附加信息要求ReShade以17行读取");
                        mlut_pp.Add("fLUT_LutAmount" + i, "17");
					}
				}
            }

            preset.SetValue("MultiLUT.fx", "PreprocessorDefinitions", mlut_pp.Select(x => x.Key + "=" + x.Value).ToArray());
			return;
        }

        /// <summary>
        /// 为绑定的预处理器定义更新Uniform变量值
        /// </summary>
        /// <param name="preset">输入预设文件，原位操作。</param>
        /// <param name="ppList">预处理器表以及对应的反查表。</param>
        public void FixBindings(IniFile preset, in PreprocessorList ppList)
		{
			var effects = preset.GetSections().Where(x => x != "").ToArray();
			foreach (var effect in effects)
			{
				if (!ppList.bindList.ContainsKey(effect))
					continue;
				preset.GetSection(effect, out var sectionData);
				if (!sectionData.ContainsKey("PreprocessorDefinitions") || sectionData["PreprocessorDefinitions"].Length == 0)
					continue;

                // <Preprocessor, UniformName>
                var effect_binds = ppList.bindList[effect];
				// <Preprocessor, Value>
				var effect_pp = sectionData["PreprocessorDefinitions"].Select(x =>
					x.Split(new[] { '=' }, 2, StringSplitOptions.None)
				).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);

				// 遍历着色器Section下的所有预处理器
				foreach (var pp in effect_pp)
				{
					if (!effect_binds.ContainsKey(pp.Key))
						continue;
					// 预处理器pp.Key有绑定，更新为 {effect_binds[pp.Key]}={pp.Value}
					preset.SetValue(effect, effect_binds[pp.Key], pp.Value);
				}
			}
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

	public class PreprocessorList
	{
		public Dictionary<string, SortedSet<string>> forwardList = new Dictionary<string, SortedSet<string>> { };
		public Dictionary<string, SortedSet<string>> reverseList = new Dictionary<string, SortedSet<string>> { };
		public Dictionary<string, Dictionary<string, string>> bindList = new Dictionary<string, Dictionary<string, string>> { };

        public PreprocessorList(IniFile preprocessorList)
		{
			preprocessorList.GetSection("Preprocessors", out var effectPrepPair);
			foreach (var pair in effectPrepPair)
			{
                // Value: 宏 Key: 着色器
                forwardList.Add(pair.Key, new SortedSet<string>(pair.Value));
				foreach (var prep in pair.Value)
				{
					if (reverseList.ContainsKey(prep))
						reverseList[prep].Add(pair.Key);
					else
						reverseList[prep] = new SortedSet<string> { pair.Key };
				}
			}
			preprocessorList.GetSection("Bindings", out var effectBindPair);
			if (effectBindPair == null)
				return;
			bindList = effectBindPair.Select(x =>
			{
				var y = x.Value.Where(z => z.Length > 0).Select(z =>
				{
					return z.Split(new[] { '=' }, 2, StringSplitOptions.None);
				}).Where(z => z.Length == 2).ToDictionary(z => z[0], z => z[1]);
				return new KeyValuePair<string, Dictionary<string, string>>(x.Key, y);
			}).ToDictionary(x => x.Key, x => x.Value);
		}
    }
}
