# xDRCal

This is a small .NET application to help check the black or white luminosity clipping points of an SDR or HDR monitor.

I mainly built this as a demo project to test out Direct2D HDR composition in a WinUI 3 project.

## Installation

DIY, for now. You'll need Visual Studio until I get a build set up here.

## Building

You'll need to install Visual Studio and the relevant workloads.

See here: https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=vs-2022-17-10

## Usage

Drag one slider to full black, and the other to almost black to see where it's possible to see detail. Do the opposite for the whites.

The bottom slider scales the test pattern by screen area percentage. Most HDR monitors get brighter when they only tasked to show bright whites on a smaller percentage of the screen. Note, however, that the "nits" readout in the app is only based on the signal we are sending to the monitor, and may not match the monitor's true capabilities, especially at 100% of the display area.