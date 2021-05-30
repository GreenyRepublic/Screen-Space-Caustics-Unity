# Screen Space Caustics
 Screen space caustic rendering implemented in Unity's standard render pipeline. Based on my 2018 master's thesis - [_Advancing Real-Time Global Illumination in Screen-Space_](https://mega.nz/file/TgIwVZLK#DVKZM9sLtvk3nYt2RG0hwhmzHfC2vW3Aa49xUroXgpk)
 
# How-To
This Unity project comes with a basic scene _FlatPlane_, in this scene you will find...

## Gimballed Camera ## 
An RTS-like camera, attached to this is the **CausticsEffect** object. On this object you'll find the script that does (almost) all the legwork, and the following sliders for playing around with it:
- **Caustic Sample Count** - Samples taken per-pixel in the caustics fragment shader
- **Caustic Sample Distance** - The maximum distance from the shaded fragment (in pixels) the fragment shader will send samples to
- **Gauss Kernel Radius** - The radius of the gaussian filter kernel box (i.e. length of box side / 2)
- **Gauss Deviation** - The standard deviation for the gaussian filter - gaussian weights are generated at the start of runtime based on this value.

## Collonade, Uffizi, Metro, Kitchen ## 
All these objects contain identical geometry, and feature one of four different reflection probes with different cubemaps attached. Enable one (and disable the others) to see the effects of different lighting conditions on the caustic patterns.
 
# Done
- Caustics rendering
- Basic gaussian blurring

# To-Do
- Implement importance sampling for caustics
- Improve blur
- Modulate blur and caustic sampling based on camera distance
- Improve random number generation
- Build a more interesting scene?

---

![image](https://user-images.githubusercontent.com/10632002/120116790-ad74ff80-c181-11eb-90a8-3af985ddd17a.png)
