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
		static string src { get; set; } = "./input";
		static string dst { get; set; } = "./output";
		static bool shouldCleanSorting { get; set; } = false;
		static bool shouldMovePreprocessors { get; set; } = false;
		static int shouldFixLUT { get; set; } = 0;

		static void Main(string[] args)
		{
			//var list = new IniFile(Assembly.GetExecutingAssembly().GetManifestResourceStream("PresetConverter.Resources.PreprocessorList.ini"));
			//var pp_list = new PreprocessorList(list);
			//var dup_list = pp_list.reverseList.Where(x => x.Value.Count > 1);
			ConvertPreset(@".\Polaris - Studio_low bloom_cold.ini");
			Console.ReadLine();
		}

		static void ArgParser(string[] args)
		{
			var i = 0;
			while (i <= args.Length)
			{
                if (args[0] == "--version" || args[0] == "--关于")
                {
					Console.WriteLine("GShade -> ReShade预设转换器 | 作者：路障MKXX\n");
                    Console.WriteLine("适合于将预设从低版本GShade迁移至高版本(5.8.0+)ReShade。\n");
                    Console.WriteLine($"版本：v{version}\n");
                    return;
                }
                if (args[0] == "--help" || args[0] == "--帮助")
				{
					Console.WriteLine(helpInfo);
					return;
				}
                if (args[i] == "--all" || args[i] == "--速通")
                {
					shouldCleanSorting = true;
					shouldFixLUT = 1;
					shouldMovePreprocessors = true;
					break;
                }
                if (args[i] == "--clean" || args[i] == "--清理")
				{
					shouldCleanSorting = true;
					i++; continue;
				}
				if (args[i] == "--migrate" || args[i] == "--迁移")
				{
					shouldMovePreprocessors = true;
					i++; continue;
				}
				if (args[i] == "--mlut" || args[i] == "--MLUT")
				{
					shouldFixLUT = 2;
					continue;
				}
				if (args[i] == "--alut" || args[i] == "--自动LUT")
				{
					shouldFixLUT = 1;
					continue;
				}
				if (args[i] == "--")
				{

				}
				Console.WriteLine($"未知参数：{args[i]}");
			}
		}

		static void ConvertPreset(string GShadePresetPath)
		{
			var source = new GShadePreset(new IniFile(new FileStream(GShadePresetPath, FileMode.Open, FileAccess.Read)));
			source.gshade_preset.filePath = GShadePresetPath;
			foreach (var flair in source.flairs.Prepend(""))
			{
				source.ExtractFlair(flair);

				source.CleanSorting(source.reshade_presets[flair]);
				source.reshade_presets[flair].SaveFile();
			}
		}

		static string helpInfo = @"常见用法示例：
将旧版预设迁移到ReShade 5.8.0+并拆分，但不做额外修复。处理文件夹in中所有文件，并输出到文件夹out。
    PresetConverter.exe --migrate -- .\in > .\out
将旧版预设迁移到ReShade 5.8.0+并拆分，清理着色器排序信息，修复MultiLUT编号问题：
    PresetConverter.exe --migrate --clean --mlut -- .\in > .\out
	
参数说明：
    --version   --版本  显示程序版本信息。

    --help      --帮助  打印此帮助信息。

    --migrate   --迁移  GS预设迁移至ReShade 5.8.0以上。迁移过程包括以下操作：
                        1. GShade预设的模板变体会被拆分为独立预设。
                        2. 在此过程中，旧版位于开头的预处理器定义将分散至相关着色器的参数区段中。
                        * 操作2依赖于手工统计的列表，不一定能够移动所有的预处理器，如有遗漏欢迎反馈。

    --clean     --清理  移除TechniqueSorting区段，让预设文件更清爽。						

    --mlut      --MLUT  新版MultiLUT支持三个Pass使用不同的自定义MultiLUT素材，旧版是共用，该参数能够
                        将旧版预设升级到新标准。
                        此外，最新的MultiLUT自定义以18行色条为准，如果旧版预设使用了自制MultiLUT且为
                        17行，则同样需要使用该参数，修补预设以让ReShade按17行读取你的MultiLUT色条。
                        如果在旧版GShade中曾经使用过“ReShade”这个MultiLUT图集，且使用了“Sepia”
                        “B&W mid constrast”“B&W high contrast”之一，则也可能需要使用该参数修复编号值。

    --alut      --ALUT  探测GS预设的FeatureLevel版本号，决定是否应用--mlut中的修复内容。
                        不保证对来自所有GShade版本的预设都能解析并正确转换。

    --all       --速通  包含--clean --migrate --alut三项操作，但不推荐起手就这么用。

	-- [SRC] > [DST]    读取[SRC]目录下的所有预设文件（不递归），转换结果输出到[DST]目录。
                        自行替换[SRC]和[DST]路径，例如写“.\input”就是本程序所处位置旁边的input文件夹。
						不写的话默认使用input和output两个文件夹。
";
	}
}
