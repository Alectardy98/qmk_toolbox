using System.Threading.Tasks;

namespace QMK_Toolbox.Views
{
    public interface IWindow
    {
        void ShowMessage(string message);
        void OnClose();
        Task OnFileOpen();
    }
}
