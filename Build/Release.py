
import os
import shutil
import re
import sys


ELEVATOR_CSPROJ = "src\\Elevator\\Elevator.csproj"
ELEVATOR_OUTPUT_FILE = "src\\Elevator\\bin\\Release\\Publish\\Elevator.exe"

OMniX_CSPROJ = "src\\OMniX.WinUI\\OMniX.WinUI.csproj"
OMniX_OUTPUT = "src\\OMniX.WinUI\\bin\\Release\Publish\\"

OMniX_Updater_CSPROJ = "src\\OMniX.AutoUpdater\\OMniX.AutoUpdater.csproj"
OMniX_Updater_OUTPUT = "src\\\\OMniX.AutoUpdater\\bin\\Release\Publish\\"
OMniX_Updater_FolderName = "OMniX - Auto Updater_New"

RELEASE_DIR = "output"
OMniX_RELEASE_DIR = "output\\OMniX"

SelfContained =  sys.argv[1] == "SelfContained" if len(sys.argv) > 1  else False
ExcludeElevator = "ExcludeElevator" in sys.argv

def checkSuccessfulExitCode(exitCode: int) -> None:
	if exitCode != 0:
		print("Exit code: " + str(exitCode))
		exit(exitCode)

def extractVersionNumber() -> str:
	with open(OMniX_CSPROJ, "r") as omnixCSPROJ:
		for line in omnixCSPROJ:
			line = line.strip()
			if line.startswith("<VersionPrefix>"):
				return re.findall("\d+\.\d+\.\d+", line)

print("PostBuild.py")
print("PWD: " + os.getcwd())
print("SelfContained: " + str(SelfContained))

versionNumber = extractVersionNumber()
if versionNumber is None or len(versionNumber) == 0:
	print("Failed to extract version number from " + OMniX_CSPROJ)
	exit(1)
versionNumber = versionNumber[0]

if (ExcludeElevator == False):
	print("Building Elevator...")
	elevatorPublishCommand = "dotnet publish " + ELEVATOR_CSPROJ + " /p:PublishProfile=FolderProfile.pubxml -c Release"
	print(elevatorPublishCommand)
	checkSuccessfulExitCode(os.system(elevatorPublishCommand))
	print()
	print("Finished building Elevator")
else:
	print("Skipping Elevator")
	print()


if (SelfContained == False):
	print("Building OMniX - Auto Updater...")
	omnixUpdaterPublishCommand = "dotnet publish " + OMniX_Updater_CSPROJ + " /p:PublishProfile=FolderProfile.pubxml -c Release"
	print(omnixUpdaterPublishCommand)
	checkSuccessfulExitCode(os.system(omnixUpdaterPublishCommand))
	print()
	print("Finished building OMniX - Auto Updater")
else:
	print("Skipping OMniX - Auto Updater build because it is not supported for self-contained build")
	print()

print("Building OMniX...")
omnixPublishCommand = "dotnet publish " + OMniX_CSPROJ + (" /p:PublishProfile=FolderProfileSelfContained.pubxml" if SelfContained else " /p:PublishProfile=FolderProfile.pubxml") + " -c Release" 
print(omnixPublishCommand)
checkSuccessfulExitCode(os.system(omnixPublishCommand))
print()
print("Finished building OMniX")

# Create release directory
os.makedirs(RELEASE_DIR, exist_ok=True)
os.makedirs(OMniX_RELEASE_DIR, exist_ok=True)

if (ExcludeElevator == False):
	print("Copying Elevator to OMniX...")
	checkSuccessfulExitCode(os.system("copy " + ELEVATOR_OUTPUT_FILE + " " + OMniX_RELEASE_DIR))
	print()
	print("Finished copying Elevator to release directory")

print("Copying OMniX to output...")
shutil.copytree(OMniX_OUTPUT, OMniX_RELEASE_DIR, dirs_exist_ok=True)
print()
print("Finished copying OMniX to release directory")

if (SelfContained == False):
	print("Copying OMniX - Auto Updater to output...")
	os.mkdir(OMniX_RELEASE_DIR + "\\" + OMniX_Updater_FolderName)
	shutil.copytree(OMniX_Updater_OUTPUT, OMniX_RELEASE_DIR + "\\" + OMniX_Updater_FolderName, dirs_exist_ok=True)
	print()
	print("Finished copying OMniX - Auto Updater to release directory")

print("Copying text files to RELEASE_DIR...")
shutil.copy("Build\\README.txt", RELEASE_DIR)
shutil.copy("CHANGELOG.md", RELEASE_DIR + "\\CHANGELOG.txt")

print("Finished copying text files to release directory")

print("Zipping release directory...")
print("7z a -t7z -xm4 OMniX.7z " + RELEASE_DIR)
releaseArchiveName = "OMniX_v" + versionNumber + ".7z"
if (SelfContained):
	releaseArchiveName = "SelfContained_" + releaseArchiveName

checkSuccessfulExitCode(os.system(f"7z a -mx4 {releaseArchiveName} .\\{RELEASE_DIR}\\*"))
print()
print("Finished zipping release directory")

env_file = os.getenv('GITHUB_ENV')
if env_file is None:
	exit(1)

with open(env_file, "a") as myfile:
    myfile.write(f"zipFile={releaseArchiveName}")

checkSuccessfulExitCode(os.system(f"7z h -scrcsha256 .\\{releaseArchiveName}"))


exit(0)



