/*
 * Script Name: IMediaShareTransport.cs
 * Description: Extension point for choosing how shared media bytes move between peers.
 * Project Role: Lets the project prefer LAN/direct transfer later without rewriting catalog or UI code.
 */

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IMediaShareTransport
{
    bool CanUseForCurrentSession();
    Task SendMediaAsync(SharedMediaEntry entry, string localPath, IReadOnlyList<ulong> targets);
}
