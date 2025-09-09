# Draekz ComfyUI Server Manager

NOTE: I have no connection to the ComfyUI team, nor collaborate with them on what I'm doing. This is purely an add-on tool I use as a quality of life thing and share it with others.

This project aims to provide a Tray Icon application for Windows which supports all of ComfyUI's configuration options for running the server, along with convenience to start/stop the server, and have access to it's log from the system tray in windows. It supports some convenience features like being able to auto-restart the server when it crashes, along with starting the manager and the server when your system starts.

System Tray Icon, Menu and Taskbar:

<img width="422" height="154" alt="Screenshot 2025-09-08 175210" src="https://github.com/user-attachments/assets/73b12fc3-1078-494b-8ea1-0797648adb7b" />

<img width="460" height="329" alt="Screenshot 2025-09-08 175218" src="https://github.com/user-attachments/assets/305781f8-6b10-4087-a838-08f96b640f89" />

<img width="135" height="69" alt="Screenshot 2025-09-08 175718" src="https://github.com/user-attachments/assets/8db606dd-a6e8-4609-80f5-a64f936f130a" />

Settings Panel:

<img width="525" height="480" alt="Screenshot 2025-09-08 175300" src="https://github.com/user-attachments/assets/28466a09-ccd6-40a9-b42c-236a92666fa0" />
<img width="525" height="480" alt="Screenshot 2025-09-08 175309" src="https://github.com/user-attachments/assets/e83fdb13-1551-49d6-ab35-8d9cdf24a326" />
<img width="525" height="490" alt="Screenshot 2025-09-08 175335" src="https://github.com/user-attachments/assets/0f18de3d-2b40-457e-8713-35969dd802a4" />
<img width="525" height="485" alt="Screenshot 2025-09-08 175341" src="https://github.com/user-attachments/assets/627501b0-912d-4a45-ba55-c8d0cf583fbd" />

Log Window:

<img width="790" height="497" alt="Screenshot 2025-09-08 175249" src="https://github.com/user-attachments/assets/2fab9c02-8360-4064-a71d-a2b1da5526c0" />

The project uses .NET 8.0 for windows, WinForms components to get something that looks integrated easily and quickly. The installer is built using WixToolset.Sdk 6.0.1.

The project was written entirely using Gemini 2.5 Pro, and is meant to be used by the Draekz ComfyUI Installer which allows easy installation of ComfyUI, the Server Manager, the Client Wrapper, a ton of nodes, models and options right from first install.

https://github.com/Draek2077/comfyui-ultimate-installer

If you are interested in ComfyUI, please visit:

https://github.com/comfyanonymous/ComfyUI
