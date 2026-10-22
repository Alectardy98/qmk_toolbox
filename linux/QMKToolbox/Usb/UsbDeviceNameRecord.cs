namespace QMK_Toolbox.Usb;

public class UsbDeviceNameRecord
{
    public string Identity { get; set; }
    public string DeviceAddress { get; set; }
    public ushort ProductId{ get; set; }
    public ushort VendorId{ get; set; }
    public ushort DeviceRevisionBcd{ get; set; }
    public string ManufacturerName { get; set; }
    public string ProductName { get; set; }
    public string SerialNumber { get; set; }
    public bool HasDfuInterface { get; set; }
}
