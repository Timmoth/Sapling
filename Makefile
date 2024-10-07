.DEFAULT_GOAL := publish

ifndef EXE
	EXE = Sapling
endif

ifeq ($(OS),Windows_NT)
	ifeq ($(PROCESSOR_ARCHITEW6432),AMD64)
		RUNTIME=win-x64
	else
		RUNTIME=win-x86
	endif
	SHELL := cmd.exe
	MKDIR_CMD := if not exist "$(subst /,\,$(OUTPUT_DIR))" mkdir "$(subst /,\,$(OUTPUT_DIR))"
	MOVE_CMD := move
	SLASH := \\
else
	UNAME_S := $(shell uname -s)
	UNAME_P := $(shell uname -p)
	ifeq ($(UNAME_S),Linux)
		RUNTIME=linux-x64
		ifneq ($(filter aarch64%,$(UNAME_P)),)
			RUNTIME=linux-arm64
		else ifneq ($(filter armv8%,$(UNAME_P)),)
			RUNTIME=linux-arm64
		else ifneq ($(filter arm%,$(UNAME_P)),)
			RUNTIME=linux-arm
		endif
	else ifeq ($(UNAME_S),Darwin)
		ifneq ($(filter arm%,$(UNAME_P)),)
			RUNTIME=osx-arm64
		else
			RUNTIME=osx-x64
		endif
	endif
	SHELL := /bin/sh
	MKDIR_CMD := if [ ! -d "$(OUTPUT_DIR)" ]; then mkdir -p $(OUTPUT_DIR); fi
	MOVE_CMD := mv
	SLASH := /
endif

# Extract the output directory from the EXE path (everything before the last slash)
OUTPUT_DIR := $(dir $(EXE))

# Extract the filename from the EXE path (everything after the last slash, without extension)
OUTPUT_EXE := $(notdir $(EXE))

publish:
	@$(MKDIR_CMD)
	dotnet publish Sapling/Sapling.csproj -c Release --runtime $(RUNTIME) --self-contained \
		-p:PublishSingleFile=true -p:DeterministicBuild=true -o $(OUTPUT_DIR) -p:DefineConstants="AVX512;Dev"

	# Renaming the generated .exe file
	@if [ "$(OS)" = "Windows_NT" ]; then \
		$(MOVE_CMD) $(subst /,$(SLASH),$(OUTPUT_DIR))Sapling.exe $(subst /,$(SLASH),$(OUTPUT_DIR))$(OUTPUT_EXE).exe; \
	else \
		$(MOVE_CMD) $(OUTPUT_DIR)Sapling $(OUTPUT_DIR)$(OUTPUT_EXE); \
	fi
