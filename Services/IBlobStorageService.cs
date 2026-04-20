using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace EasyApplyAPI.Services
{
    public interface IBlobStorageService
    {
        Task<string> UploadFileAsync(IFormFile file);
        Task<(byte[] Content, string ContentType)> DownloadFileAsync(string fileUrl);
        Task DeleteFileAsync(string fileUrl);
    }
}
