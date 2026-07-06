{
  description = "SCARL - Professional Image Reconstruction & AI Upscaler";

  inputs = {
    nixpkgs.url = "github:nixos/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { inherit system; };
      in
      {
        packages.default = pkgs.stdenv.mkDerivation {
          pname = "scarl";
          version = "1.0.0";
          src = ./.;

          nativeBuildInputs = [
            pkgs.dotnet-sdk
            pkgs.cargo
            pkgs.rustc
            pkgs.pkg-config
            pkgs.makeWrapper
            pkgs.autoPatchelfHook
          ];

          buildInputs = [
            pkgs.openssl
            pkgs.fontconfig
            pkgs.xorg.libX11
            pkgs.xorg.libXcursor
            pkgs.xorg.libXext
            pkgs.xorg.libXrandr
            pkgs.xorg.libXi
            pkgs.libGL
          ];

          buildPhase = ''
            # Compile Rust FFI Core
            cargo build --release

            # Publish C# Avalonia App
            dotnet publish Scarl.UI/Scarl.UI.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o $out/share/scarl
          '';

          installPhase = ''
            mkdir -p $out/bin
            
            # Copy compiled native shared library to published share directory
            cp target/release/libscarl_core.so $out/share/scarl/

            # Create startup wrapper script to resolve X11, OpenGL, and FontConfig dependencies
            makeWrapper $out/share/scarl/Scarl.UI $out/bin/scarl \
              --prefix LD_LIBRARY_PATH : "${pkgs.lib.makeLibraryPath [
                pkgs.fontconfig
                pkgs.xorg.libX11
                pkgs.xorg.libXcursor
                pkgs.xorg.libXext
                pkgs.xorg.libXrandr
                pkgs.xorg.libXi
                pkgs.libGL
              ]}"
          '';
        };

        devShells.default = pkgs.mkShell {
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
        };
      }
    );
}
