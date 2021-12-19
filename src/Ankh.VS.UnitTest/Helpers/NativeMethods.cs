using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AnkhSvn_UnitTestProject.Helpers
{
    using LCID = UInt32;
    using LANGID = UInt16;
    static class NativeMethods
    {
        [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
        public static extern UInt16 SetThreadUILanguage(UInt16 LangId);

        [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern Boolean GetThreadPreferredUILanguages(
            UInt32 dwFlags,
            out UInt32 pulNumLanguages,
            IntPtr pwszLanguagesBuffer,
            ref UInt32 pcchLanguagesBuffer);

        public const uint
            MUI_LANGUAGE_ID = 0x4,      // Use traditional language ID convention
            MUI_LANGUAGE_NAME = 0x8,  // Use ISO language (culture) name convention
            MUI_MERGE_SYSTEM_FALLBACK = 0x10,  // GetThreadPreferredUILanguages merges in parent and base languages
            MUI_MERGE_USER_FALLBACK = 0x20,  // GetThreadPreferredUILanguages merges in user preferred languages
            MUI_UI_FALLBACK = MUI_MERGE_SYSTEM_FALLBACK | MUI_MERGE_USER_FALLBACK,
            MUI_THREAD_LANGUAGES = 0x40,  // GetThreadPreferredUILanguages merges in user preferred languages
            MUI_CONSOLE_FILTER = 0x100,  // SetThreadPreferredUILanguages takes on console specific behavior
            MUI_COMPLEX_SCRIPT_FILTER = 0x200,  // SetThreadPreferredUILanguages takes on complex script specific behavior
            MUI_RESET_FILTERS = 0x001,  // Reset MUI_CONSOLE_FILTER and MUI_COMPLEX_SCRIPT_FILTER
            MUI_USER_PREFERRED_UI_LANGUAGES = 0x10,  // GetFileMUIPath returns the MUI files for the languages in the fallback list
            MUI_USE_INSTALLED_LANGUAGES = 0x20,  // GetFileMUIPath returns all the MUI files installed in the machine
            MUI_USE_SEARCH_ALL_LANGUAGES = 0x40,  // GetFileMUIPath returns all the MUI files irrespective of whether language is installed
            MUI_LANG_NEUTRAL_PE_FILE = 0x100,  // GetFileMUIPath returns target file with .mui extension
            MUI_NON_LANG_NEUTRAL_FILE = 0x200,  // GetFileMUIPath returns target file with same name as source
            MUI_MACHINE_LANGUAGE_SETTINGS = 0x400,
            MUI_FILETYPE_NOT_LANGUAGE_NEUTRAL = 0x001, // GetFileMUIInfo found a non-split resource file
            MUI_FILETYPE_LANGUAGE_NEUTRAL_MAIN = 0x002, // GetFileMUIInfo found a LN main module resource file
            MUI_FILETYPE_LANGUAGE_NEUTRAL_MUI = 0x004, // GetFileMUIInfo found a LN MUI module resource file
            MUI_QUERY_TYPE = 0x001, // GetFileMUIInfo will look for the type of the resource file
            MUI_QUERY_CHECKSUM = 0x002, // GetFileMUIInfo will look for the checksum of the resource file
            MUI_QUERY_LANGUAGE_NAME = 0x004, // GetFileMUIInfo will look for the culture of the resource file
            MUI_QUERY_RESOURCE_TYPES = 0x008, // GetFileMUIInfo will look for the resource types of the resource file
            MUI_FILEINFO_VERSION = 0x001; // Version of FILEMUIINFO structure used with GetFileMUIInfo

        [DllImport("Kernel32.dll")]
        public static extern LCID GetThreadLocale();
        [DllImport("Kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadLocale([In] LCID Locale);

        public static LANGID MAKELANGID(int p, int s) => (UInt16)((((UInt16)(s)) << 10) | (UInt16)(p));

        public const int LANG_ENGLISH = 0x09;
        public const int SUBLANG_ENGLISH_US = 0x01;    // English (USA)
    }
}
