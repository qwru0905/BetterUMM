using System;
using System.IO;
using Newtonsoft.Json;
using BetterUMM.Services;

namespace BetterUMM.Models
{
    public class AppSettings
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Info;
    }
}
