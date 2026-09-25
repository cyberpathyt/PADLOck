namespace PADLOck.Models;

public sealed class Device
{
    public Device(string serial, string transportId)
    {
        Serial = serial;
        TransportId = transportId;
    }

    public string Serial { get; }
    public string TransportId { get; }

    public string Model { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string AndroidVersion { get; set; } = "";
    public string DeviceOwner { get; set; } = "";

    public bool InfoLoaded { get; set; }
    public bool InfoLoading { get; set; }

    public bool IsChecked { get; set; } = true;
    public bool IsBusy { get; set; }
    public string Activity { get; set; } = "";
    public int Done { get; set; }
    public int Total { get; set; }
    public int Errors { get; set; }

    public bool IsReady => State == "device";
    public bool IsKioskOwner => DeviceOwner == "com.freekiosk";

    public string Title => Name.Length > 0 ? Name : Model.Length > 0 ? Model : Serial;
    public string DisplayName => Title == Serial ? Serial : $"{Title} · {Serial}";
}
