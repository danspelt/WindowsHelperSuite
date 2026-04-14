using CoreWriter = WindowsHelperSuite.Core.Models.Writer;
using EngineModels = WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Prediction;

public static class WriterEngineSnapshotMapper
{
    public static EngineModels.WriterContextSnapshot ToWriterEngine(
        CoreWriter.WriterContextSnapshot core,
        bool headsetConnected = false) =>
        new()
        {
            ProcessName = core.ForegroundProcessName ?? "",
            WindowTitle = core.ForegroundWindowTitle ?? "",
            TypingMode = core.Mode switch
            {
                CoreWriter.WriterTypingMode.Chat => EngineModels.WriterTypingMode.Chat,
                CoreWriter.WriterTypingMode.Email => EngineModels.WriterTypingMode.Email,
                CoreWriter.WriterTypingMode.Development => EngineModels.WriterTypingMode.Code,
                _ => EngineModels.WriterTypingMode.Neutral
            },
            HeadsetConnected = headsetConnected
        };
}
