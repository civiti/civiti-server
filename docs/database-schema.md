# Civica Database Schema Design

## 🎯 Overview

Complete PostgreSQL database schema for the Civica platform, designed to support all frontend features including user authentication, issue management, gamification, and admin workflows.

## 🏗️ Database Architecture

### Core Design Principles

1. **Supabase Integration**: Uses Supabase's built-in `auth.users` table and extends with custom user profiles
2. **Performance Optimized**: Strategic indexes on frequently queried columns
3. **Data Integrity**: Comprehensive foreign key constraints and check constraints
4. **Romanian Localization**: UTF-8 support for Romanian diacritics
5. **Scalability**: Designed to handle thousands of users and issues
6. **Audit Trail**: Created/updated timestamps on all entities

### Schema Overview

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   auth.users    │    │  user_profiles  │    │     issues      │
│   (Supabase)    │◄──►│                 │◄──►│                 │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                              │                        │
                              ▼                        ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   user_badges   │    │ user_achievements│    │  issue_photos   │
│                 │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
        │                      │
        ▼                      ▼
┌─────────────────┐    ┌─────────────────┐
│     badges      │    │  achievements   │
│                 │    │                 │
└─────────────────┘    └─────────────────┘
```

## 📋 Table Definitions

### 1. User Profiles Table

Extends Supabase auth with gamification and profile data.

```sql
-- User profiles extending Supabase auth
CREATE TABLE public.user_profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    supabase_user_id UUID UNIQUE NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    email VARCHAR(255) NOT NULL,
    display_name VARCHAR(255) NOT NULL,
    photo_url TEXT,
    
    -- Location data (hardcoded for MVP: București, Sector 5)
    county VARCHAR(100) NOT NULL DEFAULT 'București',
    city VARCHAR(100) NOT NULL DEFAULT 'București',
    district VARCHAR(100) NOT NULL DEFAULT 'Sector 5',
    
    -- Profile preferences
    residence_type VARCHAR(20) CHECK (residence_type IN ('apartment', 'house', 'business')),
    issue_updates_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    community_news_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    monthly_digest_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    achievements_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    
    -- Gamification data
    points INTEGER NOT NULL DEFAULT 0 CHECK (points >= 0),
    level INTEGER NOT NULL DEFAULT 1 CHECK (level >= 1),
    
    -- Statistics
    issues_reported INTEGER NOT NULL DEFAULT 0 CHECK (issues_reported >= 0),
    issues_resolved INTEGER NOT NULL DEFAULT 0 CHECK (issues_resolved >= 0),
    community_votes INTEGER NOT NULL DEFAULT 0 CHECK (community_votes >= 0),
    comments_given INTEGER NOT NULL DEFAULT 0 CHECK (comments_given >= 0),
    helpful_comments INTEGER NOT NULL DEFAULT 0 CHECK (helpful_comments >= 0),
    quality_score DECIMAL(5,2) NOT NULL DEFAULT 0 CHECK (quality_score >= 0 AND quality_score <= 100),
    approval_rate DECIMAL(5,2) NOT NULL DEFAULT 0 CHECK (approval_rate >= 0 AND approval_rate <= 100),
    
    -- Streaks
    current_login_streak INTEGER NOT NULL DEFAULT 0 CHECK (current_login_streak >= 0),
    longest_login_streak INTEGER NOT NULL DEFAULT 0 CHECK (longest_login_streak >= 0),
    current_voting_streak INTEGER NOT NULL DEFAULT 0 CHECK (current_voting_streak >= 0),
    longest_voting_streak INTEGER NOT NULL DEFAULT 0 CHECK (longest_voting_streak >= 0),
    last_activity_date TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    email_verified BOOLEAN NOT NULL DEFAULT FALSE
);

-- Indexes for performance
CREATE INDEX idx_user_profiles_supabase_user_id ON public.user_profiles(supabase_user_id);
CREATE INDEX idx_user_profiles_email ON public.user_profiles(email);
CREATE INDEX idx_user_profiles_district ON public.user_profiles(district);
CREATE INDEX idx_user_profiles_points ON public.user_profiles(points DESC);
CREATE INDEX idx_user_profiles_level ON public.user_profiles(level DESC);

-- Row Level Security (RLS)
ALTER TABLE public.user_profiles ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Users can view own profile" ON public.user_profiles
    FOR SELECT USING (auth.uid() = supabase_user_id);

CREATE POLICY "Users can update own profile" ON public.user_profiles
    FOR UPDATE USING (auth.uid() = supabase_user_id);

CREATE POLICY "Anyone can create profile during registration" ON public.user_profiles
    FOR INSERT WITH CHECK (auth.uid() = supabase_user_id);
```

### 2. Issues Table

Core table for civic issues reported by users.

```sql
-- Issues reported by users
CREATE TABLE public.issues (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES public.user_profiles(id) ON DELETE CASCADE,
    
    -- Issue content
    title VARCHAR(500) NOT NULL,
    description TEXT NOT NULL,
    category VARCHAR(50) NOT NULL CHECK (category IN ('infrastructure', 'environment', 'transportation', 'public_services', 'safety', 'other')),
    
    -- Location data
    address TEXT NOT NULL,
    latitude DOUBLE PRECISION NOT NULL CHECK (latitude >= -90 AND latitude <= 90),
    longitude DOUBLE PRECISION NOT NULL CHECK (longitude >= -180 AND longitude <= 180),
    location_accuracy INTEGER CHECK (location_accuracy > 0),
    neighborhood VARCHAR(100),
    landmark VARCHAR(200),
    
    -- Issue metadata
    urgency VARCHAR(20) NOT NULL DEFAULT 'medium' CHECK (urgency IN ('low', 'medium', 'high', 'urgent')),
    status VARCHAR(30) NOT NULL DEFAULT 'submitted' CHECK (status IN ('draft', 'submitted', 'under_review', 'approved', 'in_progress', 'resolved', 'rejected')),
    emails_sent INTEGER NOT NULL DEFAULT 0 CHECK (emails_sent >= 0),
    
    -- Extended description fields
    current_situation TEXT,
    desired_outcome TEXT,
    community_impact TEXT,
    
    -- AI-generated content
    ai_generated_description TEXT,
    ai_proposed_solution TEXT,
    ai_confidence DECIMAL(3,2) CHECK (ai_confidence >= 0 AND ai_confidence <= 1),
    
    -- Admin workflow
    admin_notes TEXT,
    rejection_reason TEXT,
    priority VARCHAR(20) NOT NULL DEFAULT 'medium' CHECK (priority IN ('low', 'medium', 'high', 'critical')),
    assigned_department VARCHAR(100),
    estimated_resolution_time VARCHAR(50),
    public_visibility BOOLEAN NOT NULL DEFAULT TRUE,
    reviewed_at TIMESTAMPTZ,
    reviewed_by VARCHAR(255),
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Indexes for performance
CREATE INDEX idx_issues_user_id ON public.issues(user_id);
CREATE INDEX idx_issues_status ON public.issues(status);
CREATE INDEX idx_issues_category ON public.issues(category);
CREATE INDEX idx_issues_urgency ON public.issues(urgency);
CREATE INDEX idx_issues_created_at ON public.issues(created_at DESC);
CREATE INDEX idx_issues_emails_sent ON public.issues(emails_sent DESC);
CREATE INDEX idx_issues_location ON public.issues USING GIST (POINT(longitude, latitude));
CREATE INDEX idx_issues_public_approved ON public.issues(status, public_visibility) WHERE status = 'approved' AND public_visibility = TRUE;

-- Composite index for main query
CREATE INDEX idx_issues_main_query ON public.issues(status, public_visibility, created_at DESC) 
    WHERE status = 'approved' AND public_visibility = TRUE;

-- Row Level Security
ALTER TABLE public.issues ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Anyone can view approved public issues" ON public.issues
    FOR SELECT USING (status = 'approved' AND public_visibility = TRUE);

CREATE POLICY "Users can view own issues" ON public.issues
    FOR SELECT USING (auth.uid() IN (
        SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
    ));

CREATE POLICY "Users can create issues" ON public.issues
    FOR INSERT WITH CHECK (auth.uid() IN (
        SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
    ));

CREATE POLICY "Users can update own draft issues" ON public.issues
    FOR UPDATE USING (
        status = 'draft' AND 
        auth.uid() IN (SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id)
    );
```

### 3. Issue Photos Table

Stores photos associated with issues.

```sql
-- Photos associated with issues
CREATE TABLE public.issue_photos (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    issue_id UUID NOT NULL REFERENCES public.issues(id) ON DELETE CASCADE,
    
    -- Photo data
    url TEXT NOT NULL,
    thumbnail_url TEXT,
    caption VARCHAR(500),
    quality VARCHAR(20) NOT NULL DEFAULT 'medium' CHECK (quality IN ('low', 'medium', 'high')),
    
    -- Photo metadata
    file_size INTEGER CHECK (file_size > 0),
    width INTEGER CHECK (width > 0),
    height INTEGER CHECK (height > 0),
    format VARCHAR(10),
    
    -- AI analysis (placeholder for future)
    ai_analysis JSONB,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Indexes
CREATE INDEX idx_issue_photos_issue_id ON public.issue_photos(issue_id);
CREATE INDEX idx_issue_photos_created_at ON public.issue_photos(created_at);

-- Row Level Security
ALTER TABLE public.issue_photos ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Anyone can view photos of approved public issues" ON public.issue_photos
    FOR SELECT USING (issue_id IN (
        SELECT id FROM public.issues 
        WHERE status = 'approved' AND public_visibility = TRUE
    ));

CREATE POLICY "Users can view photos of own issues" ON public.issue_photos
    FOR SELECT USING (issue_id IN (
        SELECT i.id FROM public.issues i 
        JOIN public.user_profiles u ON i.user_id = u.id
        WHERE u.supabase_user_id = auth.uid()
    ));
```

### 4. Badges System

Gamification badges that users can earn.

```sql
-- Badge definitions
CREATE TABLE public.badges (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT NOT NULL,
    icon_url TEXT,
    category VARCHAR(30) NOT NULL CHECK (category IN ('starter', 'progress', 'achievement', 'special')),
    rarity VARCHAR(20) NOT NULL DEFAULT 'common' CHECK (rarity IN ('common', 'uncommon', 'rare', 'epic', 'legendary')),
    
    -- Requirements (could be expanded to complex rules)
    requirement_type VARCHAR(50),
    requirement_value INTEGER,
    requirement_description TEXT,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- User badges (junction table)
CREATE TABLE public.user_badges (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES public.user_profiles(id) ON DELETE CASCADE,
    badge_id UUID NOT NULL REFERENCES public.badges(id) ON DELETE CASCADE,
    earned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    UNIQUE(user_id, badge_id)
);

-- Indexes
CREATE INDEX idx_badges_category ON public.badges(category);
CREATE INDEX idx_badges_rarity ON public.badges(rarity);
CREATE INDEX idx_user_badges_user_id ON public.user_badges(user_id);
CREATE INDEX idx_user_badges_badge_id ON public.user_badges(badge_id);
CREATE INDEX idx_user_badges_earned_at ON public.user_badges(earned_at DESC);

-- Initial badge data
INSERT INTO public.badges (id, name, description, icon_url, category, rarity, requirement_type, requirement_value, requirement_description) VALUES
('11111111-1111-1111-1111-111111111111', 'Civic Starter', 'Reported your first community issue', '/assets/badges/civic-starter.svg', 'starter', 'common', 'issues_reported', 1, 'Report your first issue'),
('22222222-2222-2222-2222-222222222222', 'Picture Perfect', 'Uploaded high-quality photos with your report', '/assets/badges/picture-perfect.svg', 'progress', 'uncommon', 'quality_photos', 3, 'Upload 3 high-quality photos'),
('33333333-3333-3333-3333-333333333333', 'Email Warrior', 'Sent your first email to authorities', '/assets/badges/email-warrior.svg', 'starter', 'common', 'emails_sent', 1, 'Send your first email'),
('44444444-4444-4444-4444-444444444444', 'Community Voice', 'Voted on 10 community issues', '/assets/badges/community-voice.svg', 'progress', 'common', 'community_votes', 10, 'Vote on 10 issues'),
('55555555-5555-5555-5555-555555555555', 'Problem Solver', '3 of your issues have been resolved', '/assets/badges/problem-solver.svg', 'achievement', 'rare', 'issues_resolved', 3, '3 issues resolved'),
('66666666-6666-6666-6666-666666666666', 'Persistent Advocate', 'Sent 50 emails to authorities', '/assets/badges/persistent-advocate.svg', 'progress', 'rare', 'emails_sent_total', 50, 'Send 50 emails total'),
('77777777-7777-7777-7777-777777777777', 'Civic Champion', 'Reached the highest level', '/assets/badges/civic-champion.svg', 'achievement', 'legendary', 'level', 5, 'Reach level 5'),
('88888888-8888-8888-8888-888888888888', 'Quality Contributor', '10 approved issues with 90%+ approval rate', '/assets/badges/quality-contributor.svg', 'achievement', 'epic', 'approval_rate', 90, '10 approved issues with 90%+ approval rate');

-- Row Level Security
ALTER TABLE public.badges ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_badges ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Anyone can view badges" ON public.badges FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Users can view own badges" ON public.user_badges FOR SELECT USING (auth.uid() IN (
    SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
));
```

### 5. Achievements System

Progressive achievements with rewards.

```sql
-- Achievement definitions
CREATE TABLE public.achievements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(100) NOT NULL,
    description TEXT NOT NULL,
    max_progress INTEGER NOT NULL DEFAULT 1,
    reward_points INTEGER NOT NULL DEFAULT 0,
    reward_badge_id UUID REFERENCES public.badges(id),
    
    -- Achievement type and requirements
    achievement_type VARCHAR(50) NOT NULL,
    requirement_data JSONB,
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- User achievements (progress tracking)
CREATE TABLE public.user_achievements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES public.user_profiles(id) ON DELETE CASCADE,
    achievement_id UUID NOT NULL REFERENCES public.achievements(id) ON DELETE CASCADE,
    progress INTEGER NOT NULL DEFAULT 0 CHECK (progress >= 0),
    completed BOOLEAN NOT NULL DEFAULT FALSE,
    completed_at TIMESTAMPTZ,
    
    UNIQUE(user_id, achievement_id)
);

-- Indexes
CREATE INDEX idx_achievements_type ON public.achievements(achievement_type);
CREATE INDEX idx_user_achievements_user_id ON public.user_achievements(user_id);
CREATE INDEX idx_user_achievements_completed ON public.user_achievements(completed, completed_at);

-- Initial achievement data
INSERT INTO public.achievements (id, title, description, max_progress, reward_points, reward_badge_id, achievement_type, requirement_data) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'First Steps', 'Report your first issue', 1, 50, '11111111-1111-1111-1111-111111111111', 'issues_reported', '{"target": 1}'),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Community Champion', 'Report 10 issues', 10, 200, NULL, 'issues_reported', '{"target": 10}'),
('cccccccc-cccc-cccc-cccc-cccccccccccc', 'Quality Contributor', 'Maintain 90% approval rate with 5+ submissions', 5, 300, '88888888-8888-8888-8888-888888888888', 'approval_rate', '{"target": 90, "min_submissions": 5}'),
('dddddddd-dddd-dddd-dddd-dddddddddddd', 'Social Connector', 'Vote on 50 community issues', 50, 150, '44444444-4444-4444-4444-444444444444', 'community_votes', '{"target": 50}');

-- Row Level Security
ALTER TABLE public.achievements ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.user_achievements ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Anyone can view achievements" ON public.achievements FOR SELECT USING (is_active = TRUE);
CREATE POLICY "Users can view own achievement progress" ON public.user_achievements FOR SELECT USING (auth.uid() IN (
    SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
));
```

### 6. Admin Actions Table

Tracks all admin decisions and actions.

```sql
-- Admin actions log
CREATE TABLE public.admin_actions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    issue_id UUID NOT NULL REFERENCES public.issues(id) ON DELETE CASCADE,
    admin_user_id UUID REFERENCES public.user_profiles(id),
    admin_supabase_id UUID REFERENCES auth.users(id),
    
    -- Action details
    action_type VARCHAR(30) NOT NULL CHECK (action_type IN ('approve', 'reject', 'request_changes', 'assign', 'comment')),
    notes TEXT,
    previous_status VARCHAR(30),
    new_status VARCHAR(30),
    
    -- Department assignment
    assigned_department VARCHAR(100),
    estimated_resolution_time VARCHAR(50),
    
    -- Audit fields
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Indexes
CREATE INDEX idx_admin_actions_issue_id ON public.admin_actions(issue_id);
CREATE INDEX idx_admin_actions_admin_user_id ON public.admin_actions(admin_user_id);
CREATE INDEX idx_admin_actions_action_type ON public.admin_actions(action_type);
CREATE INDEX idx_admin_actions_created_at ON public.admin_actions(created_at DESC);

-- Row Level Security
ALTER TABLE public.admin_actions ENABLE ROW LEVEL SECURITY;

-- RLS Policy (admin only)
CREATE POLICY "Admins can view all admin actions" ON public.admin_actions
    FOR ALL USING (auth.jwt()->>'role' = 'admin' OR auth.uid() = admin_supabase_id);
```

### 7. Email Tracking Table

Tracks emails sent by users to authorities.

```sql
-- Email tracking for statistics
CREATE TABLE public.email_trackings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    issue_id UUID NOT NULL REFERENCES public.issues(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES public.user_profiles(id) ON DELETE CASCADE,
    
    -- Email details
    authority_email VARCHAR(255) NOT NULL,
    authority_name VARCHAR(200),
    
    -- Tracking data
    sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    -- Prevent duplicate tracking
    UNIQUE(issue_id, user_id, authority_email)
);

-- Indexes
CREATE INDEX idx_email_trackings_issue_id ON public.email_trackings(issue_id);
CREATE INDEX idx_email_trackings_user_id ON public.email_trackings(user_id);
CREATE INDEX idx_email_trackings_sent_at ON public.email_trackings(sent_at DESC);

-- Row Level Security
ALTER TABLE public.email_trackings ENABLE ROW LEVEL SECURITY;

-- RLS Policies
CREATE POLICY "Users can view own email tracking" ON public.email_trackings
    FOR SELECT USING (auth.uid() IN (
        SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
    ));

CREATE POLICY "Users can insert own email tracking" ON public.email_trackings
    FOR INSERT WITH CHECK (auth.uid() IN (
        SELECT supabase_user_id FROM public.user_profiles WHERE id = user_id
    ));
```

## 🚀 Database Functions and Triggers

### 1. Update Timestamps Trigger

```sql
-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Apply trigger to relevant tables
CREATE TRIGGER update_user_profiles_updated_at 
    BEFORE UPDATE ON public.user_profiles 
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_issues_updated_at 
    BEFORE UPDATE ON public.issues 
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
```

### 2. Gamification Functions

```sql
-- Function to award points and check level progression
CREATE OR REPLACE FUNCTION award_points(p_user_id UUID, p_points INTEGER, p_reason TEXT)
RETURNS VOID AS $$
DECLARE
    new_total INTEGER;
    new_level INTEGER;
BEGIN
    -- Update points
    UPDATE public.user_profiles 
    SET points = points + p_points
    WHERE id = p_user_id;
    
    -- Get new total
    SELECT points INTO new_total 
    FROM public.user_profiles 
    WHERE id = p_user_id;
    
    -- Calculate new level (every 1000 points = 1 level)
    new_level := (new_total / 1000) + 1;
    
    -- Update level if changed
    UPDATE public.user_profiles 
    SET level = new_level
    WHERE id = p_user_id AND level < new_level;
    
    -- Log the point award (could add a points_log table)
    -- INSERT INTO points_log (user_id, points, reason, created_at) 
    -- VALUES (p_user_id, p_points, p_reason, NOW());
END;
$$ LANGUAGE plpgsql;

-- Function to check and award badges
CREATE OR REPLACE FUNCTION check_badge_eligibility(p_user_id UUID)
RETURNS VOID AS $$
DECLARE
    user_stats RECORD;
    badge_record RECORD;
BEGIN
    -- Get user statistics
    SELECT 
        issues_reported,
        issues_resolved,
        community_votes,
        approval_rate,
        points,
        level
    INTO user_stats
    FROM public.user_profiles 
    WHERE id = p_user_id;
    
    -- Check each badge requirement
    FOR badge_record IN 
        SELECT id, requirement_type, requirement_value
        FROM public.badges 
        WHERE is_active = TRUE 
        AND id NOT IN (
            SELECT badge_id 
            FROM public.user_badges 
            WHERE user_id = p_user_id
        )
    LOOP
        -- Award badge based on requirement type
        CASE badge_record.requirement_type
            WHEN 'issues_reported' THEN
                IF user_stats.issues_reported >= badge_record.requirement_value THEN
                    INSERT INTO public.user_badges (user_id, badge_id) 
                    VALUES (p_user_id, badge_record.id);
                END IF;
            WHEN 'community_votes' THEN
                IF user_stats.community_votes >= badge_record.requirement_value THEN
                    INSERT INTO public.user_badges (user_id, badge_id) 
                    VALUES (p_user_id, badge_record.id);
                END IF;
            WHEN 'level' THEN
                IF user_stats.level >= badge_record.requirement_value THEN
                    INSERT INTO public.user_badges (user_id, badge_id) 
                    VALUES (p_user_id, badge_record.id);
                END IF;
        END CASE;
    END LOOP;
END;
$$ LANGUAGE plpgsql;
```

### 3. Issue Statistics Function

```sql
-- Function to get issue statistics
CREATE OR REPLACE FUNCTION get_issue_stats(p_issue_id UUID)
RETURNS TABLE (
    total_emails INTEGER,
    unique_users INTEGER,
    days_since_created INTEGER,
    authorities_contacted INTEGER
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        i.emails_sent as total_emails,
        COUNT(DISTINCT et.user_id)::INTEGER as unique_users,
        (EXTRACT(EPOCH FROM (NOW() - i.created_at)) / 86400)::INTEGER as days_since_created,
        COUNT(DISTINCT et.authority_email)::INTEGER as authorities_contacted
    FROM public.issues i
    LEFT JOIN public.email_trackings et ON i.id = et.issue_id
    WHERE i.id = p_issue_id
    GROUP BY i.id, i.emails_sent, i.created_at;
END;
$$ LANGUAGE plpgsql;
```

## 📊 Views for Common Queries

### 1. User Leaderboard View

```sql
-- View for user leaderboard
CREATE VIEW public.user_leaderboard AS
SELECT 
    id,
    display_name,
    points,
    level,
    issues_reported,
    issues_resolved,
    approval_rate,
    ROW_NUMBER() OVER (ORDER BY points DESC) as rank
FROM public.user_profiles
WHERE points > 0
ORDER BY points DESC;
```

### 2. Issue Summary View

```sql
-- View for issue summary with statistics
CREATE VIEW public.issue_summary AS
SELECT 
    i.*,
    up.display_name as user_name,
    COUNT(ip.id) as photo_count,
    COUNT(DISTINCT et.user_id) as unique_email_senders,
    COUNT(DISTINCT et.authority_email) as authorities_contacted
FROM public.issues i
JOIN public.user_profiles up ON i.user_id = up.id
LEFT JOIN public.issue_photos ip ON i.id = ip.issue_id
LEFT JOIN public.email_trackings et ON i.id = et.issue_id
WHERE i.status = 'approved' AND i.public_visibility = TRUE
GROUP BY i.id, up.display_name;
```

## 🔧 Migration Scripts

### Initial Migration

```sql
-- Migration: 001_initial_schema.sql
BEGIN;

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Enable PostGIS for location queries (optional)
-- CREATE EXTENSION IF NOT EXISTS postgis;

-- Create all tables in order
-- (All table creation scripts from above)

-- Create indexes
-- (All index creation scripts from above)

-- Create functions and triggers
-- (All function creation scripts from above)

-- Insert initial data
-- (All seed data scripts from above)

COMMIT;
```

### Sample Data Migration

```sql
-- Migration: 002_sample_data.sql
BEGIN;

-- Insert sample user profiles (for testing)
INSERT INTO public.user_profiles (
    id,
    supabase_user_id,
    email,
    display_name,
    county,
    city,
    district,
    points,
    level,
    issues_reported
) VALUES 
('ffffffff-ffff-ffff-ffff-ffffffffffff', 'sample-user-1', 'ion.popescu@example.com', 'Ion Popescu', 'București', 'București', 'Sector 5', 150, 1, 2),
('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 'sample-user-2', 'maria.ionescu@example.com', 'Maria Ionescu', 'București', 'București', 'Sector 5', 75, 1, 1);

-- Insert sample issues
INSERT INTO public.issues (
    id,
    user_id,
    title,
    description,
    category,
    address,
    latitude,
    longitude,
    urgency,
    status,
    emails_sent,
    current_situation,
    desired_outcome,
    community_impact,
    public_visibility
) VALUES 
(
    'dddddddd-dddd-dddd-dddd-dddddddddddd',
    'ffffffff-ffff-ffff-ffff-ffffffffffff',
    'Trotuar deteriorat pe Strada Libertății',
    'Trotuarul este grav deteriorat cu gropi mari care pun în pericol siguranța pietonilor.',
    'infrastructure',
    'Strada Libertății nr. 45, Sector 5, București',
    44.4165,
    26.0765,
    'high',
    'approved',
    47,
    'Trotuarul prezintă multiple gropi și fisuri care fac deplasarea periculoasă.',
    'Refacerea completă a trotuarului pe o lungime de 100m.',
    'Peste 500 de locuitori folosesc zilnic acest trotuar pentru acces la școală și magazine.',
    true
),
(
    'cccccccc-cccc-cccc-cccc-cccccccccccc',
    'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
    'Lipsă iluminat public în Parcul Copiilor',
    'Parcul nu are iluminat funcțional, creând probleme de siguranță după lăsarea serii.',
    'infrastructure',
    'Parcul Copiilor, Strada Primăverii, Sector 5',
    44.4128,
    26.0742,
    'urgent',
    'approved',
    128,
    'Toate lămpile din parc sunt nefuncționale de peste 3 luni.',
    'Instalarea unui sistem modern de iluminat LED în tot parcul.',
    'Parcul este folosit de peste 200 de familii, dar devine inutilizabil după ora 17:00 în sezonul rece.',
    true
);

COMMIT;
```

## 🔍 Query Examples

### Common Frontend Queries

```sql
-- Get paginated issues for main page
SELECT 
    i.id,
    i.title,
    LEFT(i.description, 200) as description,
    i.category,
    i.address,
    i.urgency,
    i.emails_sent,
    i.created_at,
    (SELECT url FROM public.issue_photos ip WHERE ip.issue_id = i.id ORDER BY ip.created_at LIMIT 1) as main_photo_url
FROM public.issues i
WHERE i.status = 'approved' 
  AND i.public_visibility = TRUE
ORDER BY i.created_at DESC
LIMIT 12 OFFSET 0;

-- Get issue detail with all related data
SELECT 
    i.*,
    up.display_name as user_name,
    COALESCE(json_agg(
        json_build_object(
            'id', ip.id,
            'url', ip.url,
            'thumbnail_url', ip.thumbnail_url,
            'caption', ip.caption
        ) ORDER BY ip.created_at
    ) FILTER (WHERE ip.id IS NOT NULL), '[]'::json) as photos
FROM public.issues i
JOIN public.user_profiles up ON i.user_id = up.id
LEFT JOIN public.issue_photos ip ON i.id = ip.issue_id
WHERE i.id = $1
  AND i.status = 'approved' 
  AND i.public_visibility = TRUE
GROUP BY i.id, up.display_name;

-- Get user profile with gamification data
SELECT 
    up.*,
    COALESCE(json_agg(
        json_build_object(
            'id', b.id,
            'name', b.name,
            'description', b.description,
            'icon_url', b.icon_url,
            'category', b.category,
            'rarity', b.rarity,
            'earned_at', ub.earned_at
        ) ORDER BY ub.earned_at DESC
    ) FILTER (WHERE b.id IS NOT NULL), '[]'::json) as badges
FROM public.user_profiles up
LEFT JOIN public.user_badges ub ON up.id = ub.user_id
LEFT JOIN public.badges b ON ub.badge_id = b.id
WHERE up.supabase_user_id = $1
GROUP BY up.id;

-- Get admin dashboard statistics
SELECT 
    COUNT(*) FILTER (WHERE status = 'submitted') as pending_review,
    COUNT(*) FILTER (WHERE status = 'approved' AND DATE(reviewed_at) = CURRENT_DATE) as approved_today,
    COUNT(*) FILTER (WHERE status = 'rejected' AND DATE(reviewed_at) = CURRENT_DATE) as rejected_today,
    AVG(EXTRACT(EPOCH FROM (reviewed_at - created_at)) / 3600) FILTER (WHERE reviewed_at IS NOT NULL) as avg_review_time_hours,
    COUNT(*) as total_submissions
FROM public.issues
WHERE created_at >= CURRENT_DATE - INTERVAL '30 days';
```

## 🚀 Performance Optimization

### Database Configuration

```sql
-- Recommended PostgreSQL settings for Civica
-- (Add to postgresql.conf or Railway environment)

-- Memory settings (adjust based on available RAM)
shared_buffers = '256MB'
effective_cache_size = '1GB'
work_mem = '16MB'
maintenance_work_mem = '256MB'

-- Connection settings
max_connections = 100

-- Performance settings
random_page_cost = 1.1
effective_io_concurrency = 200

-- Logging for debugging
log_statement = 'mod'
log_duration = on
log_line_prefix = '%t [%p]: [%l-1] user=%u,db=%d,app=%a,client=%h '

-- Enable query plan analysis
log_min_duration_statement = 1000
```

### Monitoring Queries

```sql
-- Monitor slow queries
SELECT 
    query,
    mean_exec_time,
    calls,
    total_exec_time,
    rows,
    100.0 * shared_blks_hit / nullif(shared_blks_hit + shared_blks_read, 0) AS hit_percent
FROM pg_stat_statements 
ORDER BY mean_exec_time DESC 
LIMIT 10;

-- Check index usage
SELECT 
    schemaname,
    tablename,
    attname as column_name,
    n_distinct,
    most_common_vals
FROM pg_stats 
WHERE schemaname = 'public' 
ORDER BY tablename, attname;

-- Monitor table sizes
SELECT 
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
FROM pg_tables 
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

## 🔒 Security Considerations

1. **Row Level Security**: All tables use RLS to ensure data access control
2. **Input Validation**: Check constraints prevent invalid data
3. **SQL Injection Prevention**: Use parameterized queries
4. **Rate Limiting**: Implement at application layer
5. **Data Encryption**: Sensitive data encrypted at rest (Railway default)
6. **Audit Trail**: All changes tracked with timestamps
7. **Role-Based Access**: Separate policies for users, admins, and anonymous access

## 📋 Backup Strategy

### Daily Backups

```bash
# Railway automatic backups are enabled by default
# Manual backup command:
pg_dump $DATABASE_URL > backup_$(date +%Y%m%d_%H%M%S).sql

# Restore command:
psql $DATABASE_URL < backup_file.sql
```

## 🎯 Success Metrics

The database schema is successful when:
- ✅ All frontend mock data structures are mapped to real tables
- ✅ Query performance is <100ms for common operations
- ✅ Row Level Security prevents unauthorized access
- ✅ Gamification system tracks user engagement accurately
- ✅ Admin workflows support all approval processes
- ✅ Database can handle 10,000+ users and issues
- ✅ Romanian text displays correctly throughout
- ✅ All constraints maintain data integrity

---

**Implementation Note**: Create tables in the order presented to satisfy foreign key dependencies. Test each migration thoroughly in a development environment before applying to production.