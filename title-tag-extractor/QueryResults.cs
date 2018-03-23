using System.Collections.Generic;
using System.Linq;

namespace TitleTagExtractor
{
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

        public int TotalRows => FlattenResults ? Data.Count : Data.Sum(m => m.GetLength(0)) + Data.Count(m => m.GetLength(0) == 0);

        public int TotalCols => ShowFileNames ? Headers.Length + 1 : Headers.Length;

        public bool TruncateLongItems { get; set; }
    }
}