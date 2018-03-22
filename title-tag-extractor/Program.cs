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
#if DEBUG
            args = new[]{
                //"-h",
                "-s", //show file names
                "-f", //flatten results,
                "-d", //display empty rows
                @"src:C:\dev\working\transformer\samples",
                "column-width:30",
                "xpaths:(//Synopsis/TypeCode[text()=\"LOGLN\" and ../LanguageCode=\"EN\"])/../*"
            };
#endif

            new Program()
                .Run(args);

            Console.WriteLine("\n");

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        private void Run(IEnumerable<string> args)
        {
            MainAsync(args.ToList()).GetAwaiter().GetResult();
        }

        private async Task MainAsync(List<string> argsList)
        {
            Debugger.Break();

            var (showFileName, displayEmptyRows, flattenResults, xPaths, src) = ParseFlags(argsList);
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
                var queryResults = new QueryResults
                {
                    Xpath = xPath,
                    ShowFileNames = showFileName,
                    DisplayEmptyRows = displayEmptyRows,
                    FlattenResults = flattenResults
                };

                foreach (var file in sampleFiles)
                {
                    try
                    {
                        var doc = await GetXDocument(file);

                        var elementGroups = doc.XPathSelectElements(xPath)
                            .GroupBy(x => x.Name)
                            .ToList();

                        //add the headers only once. They're all the same for each xpath.
                        if (queryResults.Headers == null)
                            queryResults.Headers = elementGroups
                                .Select(e => e.Key.LocalName.ToString())
                                .ToArray();

                        //add the file name
                        queryResults.FileNames.Add(file.Name);

                        var elements = elementGroups
                            .Select(g => g.ToList())
                            .ToList();

                        if (!elementGroups.Any())
                        {
                            //add an empty matrix. This file has no matches.
                            queryResults.Data.Add(new string[0, 0]);
                            continue;
                        }

                        //create the matrix for this file
                        var totalRows = elements[0].Count;
                        var totalColumns = elements.Count;
                        var matrix = new string[totalRows, totalColumns];

                        for (var i = 0; i < totalRows; i++)
                            for (var j = 0; j < totalColumns; j++)
                                matrix[i, j] = elements[j][i].Value;

                        //add this matrix (file results) to the results (xpath query)
                        queryResults.Data.Add(matrix);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{file.Name} -> Failed!. Error: {e.Message}");
                    }
                }

                PrintResults(queryResults, showFileName);

                DrawLine("Total items", queryResults.Data.Count, true);
                Console.WriteLine();
            }
        }

        private static void PrintResults(QueryResults queryResults, bool showFileName)
        {
            DrawLine("XPATH", queryResults.Xpath);

            //write headers only for the first file
            var firstColumnHeader = showFileName ? new[] { "File Name" } : new string[0];
            var totalColumns = firstColumnHeader.Length + queryResults.Headers.Length;
            var columnPadding = Console.BufferWidth / totalColumns - 1;

            var headers = firstColumnHeader
                .Concat(queryResults.Headers)
                .Select(x => x.PadRight(columnPadding))
                .ToList();

            foreach (var _ in headers)
                queryResults.ColumnPaddings.Add(columnPadding);
            if (showFileName)
            {
                //var maxFileNameLength = queryResults.FileNames.Max(fn => fn.Length);
                //maxFileNameLength = Math.Min(maxFileNameLength, columnPadding);
                queryResults.ColumnPaddings.Add(columnPadding);
            }

            DrawLine(content: string.Join("|", headers));

            for (var i = 0; i < queryResults.Data.Count; i++)
            {
                var matrix = queryResults.Data[i];

                var flattenedMatrix = new string[1, matrix.GetLength(1)];
                if (queryResults.FlattenResults && matrix.GetLength(0) > 1)
                {
                    //flatten this matrix before print it.
                    for (var col = 0; col < matrix.GetLength(1); col++)
                    {
                        var colValues = new List<string>();
                        for (var row = 0; row < matrix.GetLength(0); row++)
                        {
                            var value = matrix[row, col];
                            if (!string.IsNullOrEmpty(value))
                                colValues.Add(value);
                        }

                        flattenedMatrix[0, col] = string.Join(", ", colValues.Distinct());
                    }
                    matrix = flattenedMatrix;
                }

                PrintMatrix(matrix, queryResults, queryResults.FileNames[i]);
            }
        }

        private static void PrintMatrix(string[,] matrix, QueryResults queryResults, string fileName)
        {
            if (matrix.GetLength(0) == 0 && queryResults.DisplayEmptyRows)
            {
                if (queryResults.ShowFileNames)
                    Console.Write(fileName.PadRight(queryResults.ColumnPaddings.Last()));

                for (var i = 0; i < queryResults.Headers.Length; i++)
                    Console.Write($"|{"".PadRight(queryResults.ColumnPaddings[i])}");

                //change the line for the time we print.
                Console.WriteLine();
            }
            else
            {
                for (var i = 0; i < matrix.GetLength(0); i++)
                {
                    //Print the filename if the flag is set.
                    if (queryResults.ShowFileNames)
                        Console.Write(fileName.PadRight(queryResults.ColumnPaddings.Last()));

                    //print all row items.
                    for (var j = 0; j < matrix.GetLength(1); j++)
                    {
                        var item = matrix[i, j];
                        if (item.Length > queryResults.ColumnPaddings[j])
                            item = item.Substring(0, queryResults.ColumnPaddings[j]);

                        Console.Write($"|{item.PadRight(queryResults.ColumnPaddings[j])}");
                    }

                    //change the line for the next row.
                    Console.WriteLine();
                }
            }
        }

        private (bool showFileName, bool displayEmptyRows, bool flattenResults, string[] xPaths, string src) ParseFlags(List<string> argsList)
        {
            var help = argsList.Contains("-h") || argsList.Contains("--help");
            if (help)
            {
                ShowUsage();
                return (false, false, false, null, null);
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

            var src = ProcessFlag(argsList, "src:", ".");
            var xPaths = ProcessArrayFlag<string>(argsList, "xpaths:");

            //return
            return (showFileName, displayEmptyRows, flattenResults, xPaths, src);
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
                "\nxpaths:<XPATH_EXP>[\\<XPATH_EXP>]         The XPAth expression (or expressions) to run over the xml files in the src dir. " +
                "\n                                         You can pass one or several back-slash-separated expressions here." +
                "\n                                         Example: //Title/Id|//Title/Name" +
                "\n                                         This will show a two-column table with Id | Name columns for each sample file on the source dir." +
                "\n");
        }

        private static void DrawLine(string header = null, object content = null, bool lineOnTop = false)
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

        private static async Task<XDocument> GetXDocument(FileSystemInfo file)
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

    internal class QueryResults
    {
        public string Xpath { get; set; }

        public List<string> FileNames { get; } = new List<string>();

        public string[] Headers { get; set; }

        public List<string[,]> Data { get; } = new List<string[,]>();

        public bool ShowFileNames { get; set; }

        public List<int> ColumnPaddings { get; } = new List<int>();

        public bool DisplayEmptyRows { get; set; }

        public bool FlattenResults { get; set; }
    }
}