"""
Title         : constants.py
Author        : Bardia Samiee
Project       : Parametric Forge
License       : MIT
Path          : rhino/plugins/DocsPlease/libs/constants.py

Description
----------------------------------------------------------------------------
Centralize all constants, metadata keys, string messages, and scale dictionaries
"""

from typing import Any, ClassVar

from System.Drawing import Color as Col

import Rhino


# --- Constants Section ----------------------------------------------------
class Constants:
    """Application-wide constants including layers, scales, and unit systems."""
    # Layer Names
    DETAIL_LAYER = "Utility_Layers::Detail_Views"
    CAPTION_LAYER = "ANNO::ANNO_DETL"

    # Layer Color (Dark Blue)
    TARGET_LAYER_COLOR = Col.FromArgb(0, 0, 191)

    # Default Labels
    SCALE_NA_LABEL = "SCALE: N/A"

    # Unit System Support
    SUPPORTED_UNIT_SYSTEMS: ClassVar[list[Any]] = [
        Rhino.UnitSystem.Inches,
        Rhino.UnitSystem.Feet,
        Rhino.UnitSystem.Millimeters,
        Rhino.UnitSystem.Meters,
    ]

    # Tolerances
    TOLERANCE = 1e-6

    # Camera Metadata Keys
    CAMERA_METADATA_KEYS: ClassVar[dict[str, str]] = {
        "location": "camera_location",
        "direction": "camera_direction",
        "up": "camera_up",
        "target": "camera_target",
        "lens_length": "lens_length_mm",
        "projection_mode": "projection_mode",
        "page_to_model_ratio": "page_to_model_ratio",
    }

    # --- Imperial Architectural Scales ------------------------------------
    ARCHITECTURAL_SCALES_IMPERIAL: ClassVar[dict[str, float]] = {
        'SCALE: 3" = 1\'-0"': 0.3333,
        'SCALE: 1-1/2" = 1\'-0"': 0.6667,
        'SCALE: 1" = 1\'-0"': 1.0,
        'SCALE: 3/4" = 1\'-0"': 1.3333,
        'SCALE: 1/2" = 1\'-0"': 2.0,
        'SCALE: 3/8" = 1\'-0"': 2.6667,
        'SCALE: 1/4" = 1\'-0"': 4.0,
        'SCALE: 3/16" = 1\'-0"': 5.3333,
        'SCALE: 1/8" = 1\'-0"': 8.0,
        'SCALE: 3/32" = 1\'-0"': 10.6667,
        'SCALE: 1/16" = 1\'-0"': 16.0,
        'SCALE: 1/32" = 1\'-0"': 32.0,
    }

    ARCHITECTURAL_SCALES_IMPERIAL_ORDER: ClassVar[list[str]] = [
        'SCALE: 3" = 1\'-0"',
        'SCALE: 1-1/2" = 1\'-0"',
        'SCALE: 1" = 1\'-0"',
        'SCALE: 3/4" = 1\'-0"',
        'SCALE: 1/2" = 1\'-0"',
        'SCALE: 3/8" = 1\'-0"',
        'SCALE: 1/4" = 1\'-0"',
        'SCALE: 3/16" = 1\'-0"',
        'SCALE: 1/8" = 1\'-0"',
        'SCALE: 3/32" = 1\'-0"',
        'SCALE: 1/16" = 1\'-0"',
        'SCALE: 1/32" = 1\'-0"',
    ]

    # --- Imperial Engineering Scales --------------------------------------
    ENGINEERING_SCALES_IMPERIAL: ClassVar[dict[str, float]] = {
        "SCALE: 1\" = 10'": 10.0,
        "SCALE: 1\" = 20'": 20.0,
        "SCALE: 1\" = 30'": 30.0,
        "SCALE: 1\" = 40'": 40.0,
        "SCALE: 1\" = 50'": 50.0,
        "SCALE: 1\" = 60'": 60.0,
        "SCALE: 1\" = 100'": 100.0,
    }

    ENGINEERING_SCALES_IMPERIAL_ORDER: ClassVar[list[str]] = [
        "SCALE: 1\" = 10'",
        "SCALE: 1\" = 20'",
        "SCALE: 1\" = 30'",
        "SCALE: 1\" = 40'",
        "SCALE: 1\" = 50'",
        "SCALE: 1\" = 60'",
        "SCALE: 1\" = 100'",
    ]

    # --- Metric Architectural Scales --------------------------------------
    ARCHITECTURAL_SCALES_METRIC: ClassVar[dict[str, float]] = {
        "SCALE: 1:1": 1.0,
        "SCALE: 1:2": 2.0,
        "SCALE: 1:5": 5.0,
        "SCALE: 1:10": 10.0,
        "SCALE: 1:20": 20.0,
        "SCALE: 1:50": 50.0,
        "SCALE: 1:100": 100.0,
        "SCALE: 1:200": 200.0,
        "SCALE: 1:500": 500.0,
        "SCALE: 1:1000": 1000.0,
    }


# --- Metadata Keys --------------------------------------------------------
class Metadata:
    """Metadata key constants for layout and detail view user strings."""
    PROJECT_NAME = "meta_project_name"
    PAGE_SCALE = "page_scale"
    DESIGNATION_LEVEL = "meta_designation_level"
    SHEET_INDICATOR = "meta_sheet_indicator"
    SUBDISCIPLINE_CODE = "meta_subdiscipline_code"
    SHEET_NAME = "meta_sheet_name"
    SHEET_NUMBER = "meta_sheet_number"
    SHEET_ID_FULL = "meta_sheet_id_full"
    REVISION_NUMBER = "meta_revision_number"
    DRAWN_BY = "meta_drawn_by"
    CHECKED_BY = "meta_checked_by"
    DATE_ISSUED = "meta_date_issued"
    COMPLETION_FLAG = "meta_page_setup_complete"

    @classmethod
    def all(cls) -> list[str]:
        """Return all metadata key values.

        Returns:
            List of all metadata key strings.
        """
        return [v for k, v in cls.__dict__.items() if k.isupper()]


# --- Designation Level (L1 / L2) ------------------------------------------
DESIGNATION_LEVEL_CHOICES = [("L1", "Level 1"), ("L2", "Level 2")]

# --- L1 Disciplines -------------------------------------------------------
DISCIPLINE_CHOICES = [
    ("A", "Architectural"),
    ("B", "Geotechnical"),
    ("C", "Civil"),
    ("D", "Process"),
    ("E", "Electrical"),
    ("F", "Fire Protection"),
    ("G", "General"),
    ("H", "Hazardous Materials"),
    ("I", "Interior"),
    ("L", "Landscape"),
    ("M", "Mechanical"),
    ("O", "Operations"),
    ("P", "Plumbing"),
    ("Q", "Equipment"),
    ("R", "Resource"),
    ("S", "Structural"),
    ("T", "Telecommunications"),
    ("V", "Survey / Mapping"),
    ("W", "Distributed Energy"),
    ("X", "Other Disciplines"),
    ("Z", "Contractor / Shop Drawings"),
]

# --- L2 sub-discipline map ------------------------------------------------
L2_CHOICES_BY_MASTER = {
    "A": [  # Architectural (A)
        ("AD", "Arch. Demolition", "Architectural Demolition"),
        ("AE", "Arch. Elements", "Architectural Elements"),
        ("AF", "Arch. Finishes", "Architectural Finishes"),
        ("AG", "Arch. Graphics", "Architectural Graphics"),
        ("AI", "Arch. Interiors", "Architectural Interiors"),
        ("AS", "Arch. Site", "Architectural Site"),
    ],
    "C": [  # Civil (C)
        ("CD", "Civ. Demolition", "Civil Demolition"),
        ("CG", "Civ. Grading", "Civil Grading"),
        ("CI", "Civ. Improvements", "Civil Improvements"),
        ("CN", "Civ. Nodes", "Civil Nodes"),
        ("CP", "Civ. Paving", "Civil Paving"),
        ("CS", "Civ. Site", "Civil Site"),
        ("CT", "Civ. Transportation", "Civil Transportation"),
        ("CU", "Civ. Utilities", "Civil Utilities"),
    ],
    "D": [  # Process (D)
        ("DA", "Proc. Airs", "Process Airs"),
        ("DC", "Proc. Chemicals", "Process Chemicals"),
        ("DD", "Proc. Demolition", "Process Demolition"),
        ("DE", "Proc. Electrical", "Process Electrical"),
        ("DG", "Proc. Gases", "Process Gases"),
        ("DI", "Proc. Instrumentation", "Process Instrumentation"),
        ("DL", "Proc. Liquids", "Process Liquids"),
        ("DM", "Proc. HPM Gases", "Process HPM Gases"),
        ("DO", "Proc. Oils", "Process Oils"),
        ("DP", "Proc. Piping", "Process Piping"),
        ("DQ", "Proc. Equipment", "Process Equipment"),  # Typo fixed
        ("DR", "Proc. Drains & Reclaims", "Process Drains and Reclaims"),
        ("DS", "Proc. Site", "Process Site"),
        ("DV", "Proc. Vacuum", "Process Vacuum"),  # Typo fixed
        ("DW", "Proc. Waters", "Process Waters"),
        ("DX", "Proc. Exhaust", "Process Exhaust"),
        ("DY", "Proc. Slurry", "Process Slurry"),
    ],
    "E": [  # Electrical (E)
        ("ED", "Elec. Demolition", "Electrical Demolition"),
        ("EI", "Elec. Instrumentation", "Electrical Instrumentation"),
        ("EL", "Elec. Lighting", "Electrical Lighting"),
        ("EP", "Elec. Power", "Electrical Power"),
        ("ES", "Elec. Site", "Electrical Site"),
        ("ET", "Elec. Telecommunications", "Electrical Telecommunications"),
        ("EY", "Elec. Aux. Sys.", "Electrical Auxiliary Systems"),
    ],
    "F": [  # Fire Protection (F)
        ("FA", "Fire Detection", "Fire Detection and Alarm"),
        ("FX", "Fire Suppression", "Fire Suppression"),
    ],
    "G": [  # General (G)
        ("GC", "Gen. Contract.", "General Contractual"),
        ("GI", "Gen. Info.", "General Informational"),
        ("GR", "Gen. Resource", "General Resource"),
    ],
    "H": [  # Hazardous Materials (H)
        ("HA", "Hazard. Asbestos", "Hazardous Materials Asbestos"),
        ("HC", "Hazard. Chemicals", "Hazardous Materials Chemicals"),
        ("HL", "Hazard. Lead", "Hazardous Materials Lead"),
        ("HP", "Hazard. PCB", "Hazardous Materials PCB"),
        ("HR", "Hazard. Refrigerants", "Hazardous Materials Refrigerants"),
    ],
    "I": [  # Interior (I)
        ("ID", "Int. Demolition", "Interior Demolition"),
        ("IF", "Int. Furnish.", "Interior Furnishings"),
        ("IG", "Int. Graphics", "Interior Graphics"),
        ("IN", "Int. Design", "Interior Design"),
    ],
    "L": [  # Landscape (L)
        ("LD", "Land. Demol.", "Landscape Demolition"),
        ("LG", "Land. Grading", "Landscape Grading"),
        ("LI", "Land. Irrig.", "Landscape Irrigation"),
        ("LL", "Land. Lighting", "Landscape Lighting"),
        ("LP", "Land. Planting", "Landscape Planting"),
        ("LR", "Land. Reloc.", "Landscape Relocation"),
        ("LS", "Land. Site", "Landscape Site"),
    ],
    "M": [  # Mechanical (M)
        ("MD", "Mech. Demol.", "Mechanical Demolition"),
        ("MH", "Mech. HVAC", "Mechanical HVAC"),
        ("MI", "Mech. Instr.", "Mechanical Instrumentation"),
        ("MP", "Mech. Piping", "Mechanical Piping"),
        ("MS", "Mech. Site", "Mechanical Site"),
    ],
    "P": [  # Plumbing (P)
        ("PD", "Plumb. Demol.", "Plumbing Demolition"),
        ("PL", "Plumb. Fix.", "Plumbing Fixtures"),
        ("PP", "Plumb. Pipe.", "Plumbing Piping"),
        ("PQ", "Plumb. Equip.", "Plumbing Equipment"),
        ("PS", "Plumb. Site", "Plumbing Site"),
    ],
    "Q": [  # Equipment (Q)
        ("QA", "Equip. Athl.", "Equipment Athletics"),
        ("QB", "Equip. Bank", "Equipment Bank"),
        ("QC", "Equip. Dry Clean.", "Equipment Dry Cleaning"),
        ("QD", "Equip. Detent.", "Equipment Detention"),
        ("QE", "Equip. Educ.", "Equipment Educational"),
        ("QF", "Equip. Food Serv.", "Equipment Food Service"),
        ("QH", "Equip. Hosp.", "Equipment Hospital"),
        ("QL", "Equip. Lab.", "Equipment Laboratory"),
        ("QM", "Equip. Maint.", "Equipment Maintenance"),
        ("QP", "Equip. Park. Lot", "Equipment Parking Lot"),
        ("QR", "Equip. Retail", "Equipment Retail"),
        ("QS", "Equip. Site", "Equipment Site"),
        ("QT", "Equip. Theatr.", "Equipment Theatrical"),
        ("QV", "Equip. Vid./Photo", "Equipment Video/Photographic"),
        ("QY", "Equip. Security", "Equipment Security"),
    ],
    "R": [  # Resource (R)
        ("RA", "Res. Arch.", "Resource Architectural"),
        ("RC", "Res. Civil", "Resource Civil"),
        ("RE", "Res. Elec.", "Resource Electrical"),
        ("RM", "Res. Mech.", "Resource Mechanical"),
        ("RR", "Res. RE", "Resource Real Estate"),
        ("RS", "Res. Struct.", "Resource Structural"),
    ],
    "S": [  # Structural (S)
        ("SB", "Struct. Sub.", "Structural Substructure"),
        ("SD", "Struct. Demol.", "Structural Demolition"),
        ("SF", "Struct. Frame", "Structural Framing"),
        ("SS", "Struct. Site", "Structural Site"),
    ],
    "T": [  # Telecommunications (T)
        ("TA", "Telecom. A/V", "Telecommunications Audio Visual"),
        ("TC", "Telecom. Clock", "Telecommunications Clock and Program"),
        ("TI", "Telecom. Intercom", "Telecommunications Intercom"),
        ("TM", "Telecom. Monitor", "Telecommunications Monitoring"),
        ("TN", "Telecom. Data Nets", "Telecommunications Data Networks"),
        ("TT", "Telecom. Tel.", "Telecommunications Telephone"),
        ("TY", "Telecom. Security", "Telecommunications Security"),
    ],
    "V": [  # Survey / Mapping (V)
        ("VA", "Survey Aerial", "Survey/Mapping Aerial"),
        ("VC", "Survey Computed", "Survey/Mapping Computed Points"),
        ("VF", "Survey Field", "Survey/Mapping Field"),
        ("VI", "Survey Digital", "Survey/Mapping Digital"),
        ("VB", "Survey Boundary", "Survey/Mapping Boundary"),
        ("VL", "Survey Land", "Survey/Mapping Land"),
        ("VN", "Survey Node pts", "Survey/Mapping Node Points"),
        ("VS", "Survey Staked pts", "Survey/Mapping Staked Points"),
        ("VU", "Survey Combined Utl", "Survey/Mapping Combined Utilities"),
    ],
    "W": [  # Distributed Energy (W)
        ("WC", "DE Civil", "Distributed Energy Civil"),
        ("WD", "DE Demolition", "Distributed Energy Demolition"),
        ("WI", "DE Intercon.", "Distributed Energy Interconnection"),
        ("WP", "DE Power", "Distributed Energy Power"),
        ("WS", "DE Struct.", "Distributed Energy Structural"),
        ("WT", "DE Telecom.", "Distributed Energy Telecommunications"),
        ("WY", "DE Aux. Sys.", "Distributed Energy Auxiliary Systems"),
    ],
}


# --- Strings Section ------------------------------------------------------
class Strings:
    """User-facing strings, prompts, and messages."""
    # Alignment
    PROMPT_SELECT_PARENT = "Select Parent Reference View"
    PROMPT_SELECT_CHILD = "Select Child View to Align"
    PROMPT_PICK_PARENT_POINT = "Pick PARENT View Reference Point"
    PROMPT_PICK_CHILD_POINT = "Pick CHILD Matching Point"
    PROMPT_DIRECTION = "Direction"
    DEFAULT_DIRECTION = "Vertical"
    DIRECTION_OPTIONS: ClassVar[list[str]] = ["Horizontal", "Vertical"]
    MSG_PARENT_CHILD_SAME = "Parent and Child details cannot be the same."
    MSG_INVALID_ALIGNMENT = "Invalid alignment choice."
    MSG_INVALID_DETAIL_SELECTED = "Invalid object selected. Please select a Detail View."
    MSG_FAILED_TRANSFORM = "Failed to apply transform to Child Detail."

    # Camera
    INFO_NO_CAMERA_METADATA = "[INFO] No previous camera metadata found. Capturing live camera settings..."
    MSG_FAILED_SET_CAMERA = "Failed to set camera metadata."

    # Detail Captioning
    PROMPT_SELECT_DETAIL = "Select a Detail View to Caption"
    PROMPT_ENTER_TITLE = "Enter Detail Caption Title"
    SCALE_NA = Constants.SCALE_NA_LABEL
    MSG_FAILED_CREATE_TEXT = "Failed to create one or more caption text elements."

    # Quick Detail
    MSG_FAILED_CREATE_DETAIL = "Failed to create Detail View."
    INFO_CREATED_NEW_DETAIL = "Created new Detail View on layer"
    INFO_CORRECTED_EXISTING_DETAILS = "Corrected {} existing Detail View(s) to '{}' layer."

    # Refresh Detail Captions
    MSG_NO_CAPTIONS_FOUND = "No caption text objects found."
    MSG_CAPTION_UPDATE_SUMMARY = "\n=== Refresh Summary ==="
    MSG_CAPTIONS_UPDATED = "Captions updated: {}"
    MSG_CAPTIONS_SKIPPED = "Captions skipped: {} (e.g., missing or invalid detail views)"
    MSG_REFRESH_COMPLETE = "=== Script Completed ==="

    PROMPT_SET_PAGE_SCALE = "Set Page Scale"
    PROMPT_EDIT_PAGE_SCALE = "Edit Page Scale"
    PROMPT_SET_CUSTOM_SCALE = "Set Custom Scale"
    PROMPT_SET_CUSTOM_SCALE_FOR_ALL = "Set Custom Scale for All Details"
    PROMPT_CHOOSE_OPERATION = "Choose an operation"
    MSG_NO_DETAILS_FOUND = "No Details found on this Layout."
    MSG_PAGE_SCALE_NOT_RECOGNIZED = "Stored Page Scale not recognized. Please reset Page Scale."
    MSG_DETAIL_SET_TO_SCALE = "Detail '{}' set to Scale: {}."

    # Architectural Scale Prompting
    PROMPT_ARCHITECTURAL_DETAIL_SCALES = "Architectural Detail Scales"
    OPTIONS_ARCHITECTURAL_OPERATIONS: ClassVar[list[str]] = [
        "Set to Custom Scale",
        "Set to Page Scale",
        "Batch Custom Scale",
        "Batch Page Scale",
    ]

    # Engineering Scale Prompting
    PROMPT_ENGINEERING_DETAIL_SCALES = "Engineering Detail Scales"
    OPTIONS_ENGINEERING_OPERATIONS: ClassVar[list[str]] = [
        "Set to Custom Scale",
        "Set to Page Scale",
        "Batch Custom Scale",
        "Batch Page Scale",
    ]

    # Operation Keywords (Shared)
    OP_SET_TO_CUSTOM_SCALE = "Set to Custom Scale"
    OP_SET_TO_PAGE_SCALE = "Set to Page Scale"
    OP_BATCH_CUSTOM_SCALE = "Batch Custom Scale"
    OP_BATCH_PAGE_SCALE = "Batch Page Scale"

    # General UX & Errors
    MSG_LAYOUT_VIEW_REQUIRED = "This script must be run in a Layout (Page) View."
    MSG_UNSUPPORTED_UNIT_SYSTEM = "This unit system is not currently supported."

    # Ortho Detail From Detail
    STEP1_PROMPT_SELECT_DETAIL = "Select a Detail View"
    STEP2_PROMPT_TITLE = "{} to New View"
    STEP4_PROMPT_INSERTION_POINT = "Select Insertion Point"
    MSG_FAILED_RETRIEVE_CAMERA_METADATA = "Failed to retrieve camera metadata from Detail View."
    MSG_INVALID_CAMERA_DIRECTION = "Unrecognized camera direction vector. Cannot determine current view."
    MSG_MUST_BE_PARALLEL_PROJECTION = "Only Parallel Projection Detail Views can be used."
    MSG_USER_CANCELLED_TARGET_VIEW = "User cancelled target projection selection."

    # Center Detail View
    PROMPT_SELECT_DETAIL_VIEW = "Select Detail View to center"
    PROMPT_SELECT_OBJECTS = "Select objects inside the Detail View to center"

    MSG_DETAIL_LOCKED = "[WARNING] Detail View is locked. Cannot modify."
    MSG_FAILED_COMPUTE_BBOX = "[ERROR] Could not compute bounding box of selected objects."

    INFO_SELECTED_DETAIL = "[INFO] Selected Detail View: {}"
    INFO_CENTER_POINT = "[INFO] Center point: {}"

    # Layout Page Manager Prompts
    PROMPT_PROJECT_NAME = "Enter Project Name:"
    PROMPT_DESIGNATION_LEVEL = "Select Designation Level:"
    PROMPT_SHEET_DISCIPLINE = "Select Sheet Discipline:"
    PROMPT_SHEET_NAME = "Enter Sheet Name (e.g., Floor Plan):"
    PROMPT_SHEET_NUMBER = "Enter Sheet Number (e.g., 1.2):"
