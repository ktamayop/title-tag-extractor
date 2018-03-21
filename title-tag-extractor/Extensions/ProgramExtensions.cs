using Microsoft.Extensions.PlatformAbstractions;

namespace TitleTagExtractor.Extensions
{
    public static class ProgramExtensions
    {
        /// <summary>
        /// Returns the application version from the platform service abstractions.
        /// </summary>
        /// <param name="program"></param>
        /// <returns></returns>
        public static string GetApplicationVersion(this Program program)
        {
            return PlatformServices.Default.Application.ApplicationVersion;
        }
    }
}
