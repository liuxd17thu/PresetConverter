using System;
using System.Collections.Generic;
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
		//static string srcVersion { get; set; } = "gs@4.1.1";
		//static string dstVersion { get; set; } = "re@5.8.0";
		static bool shouldCleanSorting { get; set; } = false;
		static bool shouldMovePreprocessors { get; set; } = false;
		static bool shouldConvert17To18 { get; set; } = false;

		static void Main(string[] args)
		{
			//var list = new IniFile(Assembly.GetExecutingAssembly().GetManifestResourceStream("PresetConverter.Resources.PreprocessorList.ini"));
			//var pp_list = new PreprocessorList(list);
			//var dup_list = pp_list.reverseList.Where(x => x.Value.Count > 1);
			var x = new string[]{ "A=1", "B=", "C" };
			var y = x.Select(a => {
				var b = a.Split(new[] { '=' }, 2, StringSplitOptions.None);
				return b[0];
			}).ToArray();
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
					shouldConvert17To18 = true;
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
				if (args[i] == "--fix17" || args[i] == "--修复17")
				{
					shouldConvert17To18 = true;
					continue;
				}
				Console.WriteLine($"未知参数：{args[i]}");
			}
		}

		static string helpInfo = @"常见用法示例：
	将旧版预设迁移到ReShade 5.8.0+并拆分，但不做额外修复：
		PresetConverter.exe --migrate
	将旧版预设迁移到ReShade 5.8.0+并拆分，清理着色器排序信息，修复MultiLUT编号问题：
		PresetConverter.exe --migrate --clean --fix17
	
参数说明：
	--version	--版本	显示程序版本信息。

	--help		--帮助	打印此帮助信息。

	--migrate	--迁移	GS预设迁移至ReShade 5.8.0以上。迁移过程包括以下操作：
						1. GShade预设的模板变体会被拆分为独立预设。
						2. 在此过程中，旧版位于开头的预处理器定义将分散至相关着色器的参数区段中。
						* 操作2依赖于手工统计的列表，不一定能够移动所有的预处理器，如有遗漏欢迎反馈。

	--clean		--清理	移除TechniqueSorting区段，让预设文件更清爽。						

	--fix17	  --修复17	最新的MultiLUT自定义以18行色条为准，如果你的旧版预设使用了自制MultiLUT且为17
						行，则可能需要使用该参数，修补预设以让ReShade按17行读取你的MultiLUT色条。
						但如果你在旧版GShade中曾经使用过“ReShade”这个MultiLUT图集，且使用了“Sepia”
						“B&W mid constrast”“B&W high contrast”之一，则也可能需要使用该参数修复编号值。

	--all		--速通	包含--clean --migrate --fix17三项操作，但不推荐起手就这么用。
";
	}
}
