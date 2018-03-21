using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using TitleTagExtractor.Extensions;

namespace TitleTagExtractor
{
    public class Program
    {
        private static void Main(string[] args)
        {
            //NOTE: To debug
            args = new[]{
                //"-h",
                "-s", //show file names
                "-f", //flatten results
                @"src:C:\dev\working\transformer\samples",
                "column-width:30",
                "xpaths:(//Synopsis/TypeCode[text()=\"LOGLN\" and ../LanguageCode=\"EN\"])/../*"
            };

            new Program()
                .Run(args);

            Console.WriteLine("\n");
        }

        private void Run(string[] args)
        {
            MainAsync(args.ToList()).GetAwaiter().GetResult();
        }

        private async Task MainAsync(List<string> argsList)
        {
            Debugger.Break();
            var (showFileName, displayEmptyRows, flattenResults, columnWidth, xPaths, src) = ParseFlags(argsList);
            if (src == null)
                return;

            var dir = new DirectoryInfo(src);
            if (!dir.Exists)
            {
                Console.WriteLine("The source directory was not found.");
                return;
            }

            var sampleFiles = dir.GetFiles("*.xml", SearchOption.TopDirectoryOnly);
            if (!sampleFiles.Any())
            {
                Console.WriteLine("The source directory contains no xml files.");
                return;
            }

            foreach (var xPath in xPaths)
            {
                var count = 0;
                DrawLine("XPATH", xPath);
                foreach (var file in sampleFiles)
                {
                    try
                    {
                        var doc = await GetXDocument(file);

                        var elementGroups = doc.XPathSelectElements(xPath)
                            .GroupBy(x => x.Name)
                            .ToList();

                        List<List<XElement>> elements = new List<List<XElement>>();
                        if (!elementGroups.Any() && !showFileName)
                            continue;

                        //write headers only for the first file
                        var columnPadding = 0;
                        var totalColumns = 0;
                        if (count++ == 0)
                        {
                            var firstColumnHeader = showFileName ? new[] { "File Name" } : new string[0];
                            var dataHeaders = elementGroups
                                .Select(e => e.Key.LocalName.ToString())
                                .ToArray();
                            totalColumns = firstColumnHeader.Length + dataHeaders.Length;
                            columnPadding = Console.BufferWidth / totalColumns - 1;
                            var headers = firstColumnHeader
                                .Concat(dataHeaders)
                                .Select(x => x.PadRight(columnPadding));

                            DrawLine("", string.Join("|", headers));
                        }

                        if (flattenResults)
                        {
                            elements.Add(new List<XElement>());

                            //fill only the first element
                            foreach (var group in elementGroups)
                                //flat this column values
                                if (group.Count() > 1)
                                {
                                    var value = string.Join(", ",
                                        group.Where(g => !string.IsNullOrEmpty(g.Value)).Select(g => g.Value)
                                            .Distinct());
                                    var xElement = new XElement(group.Key)
                                    {
                                        Value = string.IsNullOrEmpty(value) ? " " : value
                                    };
                                    elements[0].Add(xElement);
                                }
                                else
                                {
                                    elements[0].Add(group.ElementAt(0));
                                }

                            List<string> columnValues = (showFileName ? new[] { file.Name } : new string[0])
                                .Concat(elements[0].Select(e => e.Value))
                                .Select(x => x.PadRight(columnPadding))
                                .ToList();

                            //Skip empty rows if the flag is set.
                            if (columnValues
                                    .Skip(showFileName ? 1 : 0)
                                    .All(string.IsNullOrEmpty) && !displayEmptyRows)
                                continue;

                            Console.WriteLine($"{string.Join("|", columnValues)}");
                        }
                        else
                        {
                            foreach (var group in elementGroups)
                                elements.Add(group.ToList());

                            //Display the matrix
                            var totalRows = elements.Count;

                            for (int i = 0; i < totalColumns; i++)
                            {
                                List<string> columnValues = (showFileName ? new[] { file.Name } : new string[0]).ToList();

                                for (var j = 0; j < totalRows; j++)
                                    columnValues.Add(elements[j][i].Value.PadRight(columnPadding));

                                //Skip empty rows if the flag is set.
                                if (columnValues
                                        .Skip(showFileName ? 1 : 0)
                                        .All(string.IsNullOrEmpty) && !displayEmptyRows)
                                    continue;

                                Console.WriteLine($"{string.Join("|", columnValues)}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{file.Name} -> Failed!. Error: {e.Message}");
                    }
                }

                DrawLine("Total items", count, true);
                Console.WriteLine();
            }
        }

        private (bool showFileName, bool displayEmptyRows, bool flattenResults, int columnWidth, string[] xPaths, string src) ParseFlags(List<string> argsList)
        {
            var help = argsList.Contains("-h") || argsList.Contains("--help");
            if (help)
            {
                ShowUsage();
                return (false, false, false, 0, null, null);
            }

            Console.WriteLine("\n" +
                              $"Title Tag Extractor v.{this.GetApplicationVersion()}" +
                              "\n-------------------------------" +
                              "\n");

            // Parse all the flags.
            var showFileName = ProcessFlag(argsList, "-s", false);
            if (!showFileName)
                showFileName = ProcessFlag(argsList, "--show-filename", false);

            var displayEmptyRows = ProcessFlag(argsList, "-d", false);
            if (!displayEmptyRows)
                displayEmptyRows = ProcessFlag(argsList, "--display-empty-rows", false);

            var flattenResults = ProcessFlag(argsList, "--flatten-results", false);
            if (!flattenResults)
                flattenResults = ProcessFlag(argsList, "-f", false);

            var columnWidth = ProcessFlag(argsList, "column-width:", 80);
            var src = ProcessFlag(argsList, "src:", ".");
            var xPaths = ProcessArrayFlag<string>(argsList, "xpaths:");

            //return
            return (showFileName, displayEmptyRows, flattenResults, columnWidth, xPaths, src);
        }

        private static void ShowUsage()
        {
            Console.WriteLine(
                "Usage: title-tag-extractor [options]" +
                "\n" +
                "\nOptions:" +
                "\n-h, --help                               Show help information." +
                "\n-s, --show-filename                      Set this flag to display the file name of the processed file at the first column of the results table." +
                "\n-d, --display-empty-rows                 Set this flag to display the rows even when no results are found." +
                "\n-f, --flatten-results                    Set this flag to display the results flattened for each file. If a query has several results, they'll be displayed on a single line, separated by commas." +
                "\nsrc:<SRC_DIR>                            The directory to read the xml files from. Defaults to Current Dir." +
                "\ncolumn-width:<COLUMN_WITH>               The width in characters of each column in the results table. Defaults 50" +
                "\nxpaths:<XPATH_EXP>[\\<XPATH_EXP>]         The XPAth expression (or expressions) to run over the xml files in the src dir. " +
                "\n                                         You can pass one or several back-slash-separated expressions here." +
                "\n                                         Example: //Title/Id|//Title/Name" +
                "\n                                         This will show a two-column table with Id | Name columns for each sample file on the source dir." +
                "\n");
        }

        private static void DrawLine(string header, object content, bool lineOnTop = false)
        {
            if (lineOnTop)
                Console.Write("-".PadRight(Console.BufferWidth, '-'));

            if (!string.IsNullOrEmpty(header))
                Console.WriteLine($"{header}: {content}");
            else
                Console.WriteLine($"{content}");

            if (!lineOnTop)
                Console.Write("-".PadRight(Console.BufferWidth, '-'));
        }

        private static async Task<XDocument> GetXDocument(FileInfo file)
        {
            var doc = XDocument.Parse(await File.ReadAllTextAsync(file.FullName));

            // Remove xmlns  
            doc.Descendants()
                .Attributes()
                .Where(x => x.IsNamespaceDeclaration)
                .Remove();

            foreach (var elem in doc.Descendants())
                elem.Name = elem.Name.LocalName;

            foreach (var attr in doc.Descendants().Attributes())
            {
                var elem = attr.Parent;
                attr.Remove();
                elem?.Add(new XAttribute(attr.Name.LocalName, attr.Value));
            }

            return doc;
        }

        private static T ProcessFlag<T>(List<string> args, string flagPrefix, T defaultValue)
        {
            T value;
            var flagContext = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (!string.IsNullOrEmpty(flagContext))
            {
                var trimmedArg = flagContext?.Replace(flagPrefix, "").Trim();

                //for a bool flag, just return true if it's present, false otherwise.
                if (typeof(T) == typeof(bool))
                    trimmedArg = "true";

                value = (T)Convert.ChangeType(trimmedArg, typeof(T));

                //delete the arg from the list
                args.RemoveAll(x => x.StartsWith(flagPrefix));
            }
            else
                value = defaultValue;

            return value;
        }

        private static T[] ProcessArrayFlag<T>(List<string> args, string flagPrefix)
        {
            var result = new List<T>();
            var flagContext = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (!string.IsNullOrEmpty(flagContext))
            {
                var trimmedArg = flagContext?
                    .Replace(flagPrefix, "")
                    .Trim();

                //split the value by " "
                var tokens = trimmedArg?.Split("\\", StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim('"').Trim());
                result.AddRange(tokens?.Select(token => (T)Convert.ChangeType(token, typeof(T))));

                //delete the arg from the list
                args.RemoveAll(x => x.StartsWith(flagPrefix));
            }

            return result.ToArray();
        }
    }
}