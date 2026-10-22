using System.Collections.Generic;
namespace QMK_Toolbox.Usb;
internal class UsbDeviceRecordComparer : IEqualityComparer<UsbDeviceNameRecord>
{
    public bool Equals(UsbDeviceNameRecord x, UsbDeviceNameRecord y) =>
        x?.Identity == y?.Identity && x?.VendorId == y?.VendorId &&
        x?.ProductId == y?.ProductId && x?.DeviceRevisionBcd == y?.DeviceRevisionBcd && x?.HasDfuInterface == y?.HasDfuInterface && x?.DeviceAddress == y?.DeviceAddress;
    public int GetHashCode(UsbDeviceNameRecord obj) => System.HashCode.Combine(
        obj.Identity, obj.VendorId, obj.ProductId, obj.DeviceRevisionBcd, obj.HasDfuInterface, obj.DeviceAddress);
}
