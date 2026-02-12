namespace Qcapi.Tray;

internal sealed record QcapiConfig(
    string IbmCloudApiKey,
    string ServiceCrn,
    string BaseUrl,
    string ApiVersion,
    string? AccountName,
    string? QiskitConfigPath
);
