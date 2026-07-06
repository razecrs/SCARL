{ pkgs ? import <nixpkgs> {} }:

pkgs.mkShell {
  buildInputs = [
    pkgs.dotnet-sdk
    pkgs.cargo
    pkgs.rustc
    pkgs.pkg-config
    pkgs.openssl
    pkgs.fontconfig
    pkgs.xorg.libX11
    pkgs.xorg.libXcursor
    pkgs.xorg.libXext
    pkgs.xorg.libXrandr
    pkgs.xorg.libXi
    pkgs.libGL
  ];

  shellHook = ''
    echo "SCARL Nix Development Shell Activated"
    export LD_LIBRARY_PATH=${pkgs.lib.makeLibraryPath [
      pkgs.fontconfig
      pkgs.xorg.libX11
      pkgs.xorg.libXcursor
      pkgs.xorg.libXext
      pkgs.xorg.libXrandr
      pkgs.xorg.libXi
      pkgs.libGL
    ]}:$LD_LIBRARY_PATH
  '';
}
