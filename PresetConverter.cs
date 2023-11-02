using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace PresetConverter
{
	internal class PresetConverter
	{
		static readonly string version = "0.1.0";
		static string srcDirectory { get; set; } = "./input";
		static string dstDirectory { get; set; } = "./output";
		static bool shouldClean { get; set; } = false;
		static bool shouldMigrate { get; set; } = false;
		static int shouldFixLUT { get; set; } = 0;
		static bool shouldFixBinding{ get; set; } = false;
		static readonly PreprocessorList ppList = new PreprocessorList(new IniFile(Assembly.GetExecutingAssembly().GetManifestResourceStream("PresetConverter.Resources.PreprocessorList.ini")));

		static void Main(string[] args)
		{
			//shouldClean = true;
			//shouldMigrate = true;
			//shouldFixLUT = 2;
			Console.WriteLine(versionInfo);
			if (ArgParser(args))
			{
				ConvertAllPresets();
				Console.WriteLine("转换结束，按回车键退出。");
                Console.ReadLine();
            }
			else
			{
				Console.WriteLine("未转换，自动退出。");
				Console.ReadLine();
			}
		}

		static bool ArgParser(string[] args)
		{
			var i = 0;
			if (args.Length == 0)
			{
				Console.WriteLine(helpInfo);
				return false;
			}
			while (i < args.Length)
			{
                if (args[0] == "--version" || args[0] == "--关于")
                {
                    return false;
                }
                if (args[0] == "--help" || args[0] == "--帮助")
				{
					Console.WriteLine(helpInfo);
					return false;
				}
                if (args[i] == "--all" || args[i] == "--速通")
                {
					shouldClean = true;
					shouldFixLUT = 1;
					shouldMigrate = true;
					shouldFixBinding = true;
					break;
                }
                if (args[i] == "--clean" || args[i] == "--清理")
				{
					shouldClean = true;
					i++; continue;
				}
				if (args[i] == "--migrate" || args[i] == "--迁移")
				{
					shouldMigrate = true;
					i++; continue;
				}
				if (args[i] == "--bind" || args[i] == "--绑定")
				{
					shouldFixBinding = true;
					i++; continue;
				}
				if (args[i] == "--mlut" || args[i] == "--MLUT")
				{
					shouldFixLUT = 2;
					i++; continue;
				}
				if (args[i] == "--alut" || args[i] == "--自动LUT")
				{
					shouldFixLUT = 1;
                    i++; continue;
                }
				if (args[i] == "--")
				{
					i++;
					if (i >= args.Length)
						return false;
					srcDirectory = args[i];
					i++;
					if (i >= args.Length || args[i] != "-")
						return false;
					i++;
					if (i >= args.Length)
						return false;
					dstDirectory = args[i];
					return true;
				}
				Console.WriteLine($"未知参数：{args[i]}");
				return false;
			}
			return true;
		}

		static void ConvertPreset(string GShadePresetPath)
		{
			var source = new GShadePreset(new IniFile(new FileStream(GShadePresetPath, FileMode.Open, FileAccess.Read)));
			if (!source.gshade_preset.HasValue("", "Techniques"))
			{
				Console.WriteLine($"ERR |{Path.GetFileName(GShadePresetPath)}不是有效的预设文件");
				return;
			}
			source.gshade_preset.filePath = GShadePresetPath;
			var baseAndFlair = source.flairs.Prepend("");

            foreach (var flair in baseAndFlair)
			{
				if (shouldMigrate)
				{
                    source.ExtractFlair(flair);
                    source.MovePreprocessors(source.reshade_presets[flair], ppList);
                }
				if (shouldClean)
				{
                    source.CleanSorting(source.reshade_presets[flair]);
                    source.RemoveUnusedTechniques(source.reshade_presets[flair]);
                }
				if (shouldFixLUT > 0)
				{
					source.FixMultiLUT(source.reshade_presets[flair], shouldFixLUT);
				}
				if (shouldFixBinding)
				{
					source.FixBindings(source.reshade_presets[flair], ppList);
				}
                source.reshade_presets[flair].filePath = Path.Combine(dstDirectory, source.reshade_presets[flair].filePath);
                source.reshade_presets[flair].SaveFile();
				if (!shouldMigrate)
					return;
			}
		}

		static void ConvertAllPresets()
		{
			if (!Directory.Exists(srcDirectory))
			{
				Console.WriteLine($"ERR |源路径{srcDirectory}不存在");
				return;
			}
			if (!Directory.Exists(dstDirectory))
			{
				Console.WriteLine($"INFO|新建路径{dstDirectory}");
				Directory.CreateDirectory(dstDirectory);
			}
			var srcPresetsPath = Directory.EnumerateFiles(srcDirectory, "*.ini", SearchOption.TopDirectoryOnly).ToArray();
			foreach (var GShadePresetPath in srcPresetsPath)
			{
				Console.WriteLine($"INFO|读取输入预设：{Path.GetFileName(GShadePresetPath)}");
				ConvertPreset(GShadePresetPath);
                Console.WriteLine("INFO|“未使用的预处理器”可能真的没有使用，也可能是本转换器内置的统计出现了遗漏\nINFO|如真的是遗漏，请携带日志与问题预设向路障MKXX反馈");
                Console.WriteLine($"----|");
			}
		}

		static string versionInfo =
			"GShade -> ReShade预设转换器 | 作者：路障MKXX\n" +
			"适合于将预设从低版本GShade迁移至高版本(5.8.0+)ReShade。\n" +
			$"版本：v{version}\n" +
			"测试版本，如出现任何故障，请向微博 @路障MKXX 反馈。";

		static string helpInfo = @"常见用法示例：
将旧版预设迁移到ReShade 5.8.0+并拆分，但不做额外修复。处理文件夹in中所有预设文件，并输出到文件夹out。
    PresetConverter.exe --migrate -- .\in - .\out
将旧版预设迁移到ReShade 5.8.0+并拆分，清理着色器排序信息，修复MultiLUT编号问题：
    PresetConverter.exe --migrate --clean --mlut -- .\in - .\out
	
参数说明：
    --version   --版本  显示程序版本信息。

    --help      --帮助  打印此帮助信息。

    --migrate   --迁移  GS预设迁移至ReShade 5.8.0以上。迁移过程包括以下操作：
                        1. GShade预设的模板变体会被拆分为独立预设。
                        2. 在此过程中，旧版位于开头的预处理器定义将分散至相关着色器的参数区段中。
                        * 操作2依赖于手工统计的列表，不一定能够移动所有的预处理器，如有遗漏欢迎反馈。

    --clean     --清理  移除TechniqueSorting区段，以及未使用的着色器参数表，让预设文件更清爽。

    --mlut      --MLUT  新版MultiLUT支持三个Pass使用不同的自定义MultiLUT素材，旧版是共用，该参数能够
                        将旧版预设升级到新标准。
                        此外，最新的MultiLUT自定义以18行色条为准，如果旧版预设使用了自制MultiLUT且为
                        17行，则同样需要使用该参数，修补预设以让ReShade按17行读取你的MultiLUT色条。
                        如果在旧版GShade中曾经使用过“ReShade”这个MultiLUT图集，且使用了“Sepia”
                        “B&W mid constrast”“B&W high contrast”之一，则也可能需要使用该参数修复编号值。

    --alut      --ALUT  探测GS预设的FeatureLevel版本号，决定是否应用--mlut中的修复内容。
                        不保证对来自所有GShade版本的预设都能解析并正确转换。
                        对于旧ReShade预设、或是被新ReShade重新保存过的旧GShade预设，建议不使用--alut，
                        而是分别尝试使用--mlut与否。

	--bind		--绑定  ui_bind语法会将uniform变量与预处理器绑定，此操作将以预处理器为准重设uniform值，
                        解决MultiLUT等着色器中，界面上的选项表示与实际画面之间的对应错误。
                        * 本操作依赖于手工统计的列表，如有绑定错误欢迎反馈。

    --all       --速通  包含--clean --migrate --alut --bind四项操作，但不推荐起手就这么用。

	-- [SRC] - [DST]    读取[SRC]目录下的所有预设文件（不递归），转换结果输出到[DST]目录。
                        自行替换[SRC]和[DST]路径，例如写“.\input”就是本程序所处位置旁边的input文件夹。
						此参数需要置于最后，不写的话默认使用input和output两个文件夹。
";
	}
}
