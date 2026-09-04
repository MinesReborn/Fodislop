using System.Runtime.CompilerServices;

// Отчёт о старте и рантайм перехода сцен намеренно internal: это устройство
// сборки, а не её контракт. Тестам они нужны целиком, поэтому доступ открыт
// им одним — тем же способом, что в Networking, AssetPipeline и Editor.
[assembly: InternalsVisibleTo("Fodinae.Tests.Editor")]
