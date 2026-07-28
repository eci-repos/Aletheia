namespace Aletheia.Repository.API.Services;

public interface IIngestionProgressSink
{
    void Report(string stage, string detail, int? percentComplete = null, bool force = false);
}
