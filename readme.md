# Desktop Ponies

8 bit ponies on your desktop.

Desktop Ponies lets you choose some ponies from [My Little Pony: Friendship is Magic](http://wikipedia.org/wiki/My_Little_Pony:_Friendship_Is_Magic) to trot around your desktop whilst you work.

The ponies will move around on screen performing a variety of custom animations. They also talk occasionally and some ponies will interact with each other.

There are 200 ponies and other characters from the show included. There is also a wide array of OCs available and you can create your own ponies too.

## Installation

Desktop Ponies works on Windows, Mac and Linux.

* [Download the latest version (v1.52)](https://github.com/RoosterDragon/Desktop-Ponies/releases/download/v1.52/Desktop.Ponies.v1.52.zip)
* Extract the files.
* Check the included readme file for further instructions.

## License

The artwork is licensed under [Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0)](http://creativecommons.org/licenses/by-nc-sa/3.0/). This means you are allowed to share and alter the artwork, provided you give credit, do not use it for commercial purposes and release it under this same license. You can find assets for the ponies and other characters in the [Content/Ponies](Content/Ponies) directory. There is a [list of credits](Content/credits.txt) in the [Content](Content) directory.

The source code is available under the same license.

## External Links

* [Ponychan Mane Thread](http://www.ponychan.net/chan/collab/res/45984.html) - Discussion of the program and canon artwork.
* [Ponychan OC Thread](http://www.ponychan.net/chan/collab/res/43607.html) - Download a collection of common OCs and other art. Includes templates and resources for creating your own ponies.
* [deviantART Group](http://desktop-pony-team.deviantart.com/) - Group maintained by contributing artists to showcase their work.

## Documentation

Some information about the file formats employed by the program can be found in the [technical documentation](techdoc.md).

## Building

This project evolved from a Windows only solution and whilst it just about runs on non-Windows machines thanks to Mono, it is not really portable in terms on building on other platforms (sorry about that).

You will need to install [Mono](http://www.mono-project.com/download/) in order to acquire the libraries for the Gtk/Cairo portion of the program that runs on non-Windows platforms. You will probably need to update the references for those dlls in each of the projects that requires them.

The Microsoft.DirectX.AudioVideoPlayback library is long since obsolete but is used to play audio. You need to install the [DirectX 9 redistributable](http://www.microsoft.com/en-us/download/details.aspx?id=35) in order to resolve this reference.

Once resolved, you have a standard Visual Studio solution split into three projects. Desktop Sprites is the library that handles rendering, Desktop Ponies is the pony specific part of the application and Release Tool is used to run image optimizers and package new releases.

If you want to use the Release Tool to optimize images, you will need to acquire the [gifsicle](http://www.lcdf.org/gifsicle/) and [pngout](http://advsys.net/ken/utils.htm) and drop them into the application directory for them to work.